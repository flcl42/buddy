using System.Globalization;
using System.Text;
using Buddy.Core.Abstractions;
using Microsoft.Data.Sqlite;

namespace Buddy.Persistence;

public sealed class BuddyDatabase : IBuddyDatabase, IDisposable
{
    private const int CurrentSchemaVersion = 5;
    private const string ResetMarkerFileName = ".schema-reset";
    private const string ArchiveStage = "archive";
    private const string CreateStage = "create";
    private static readonly Encoding Utf8WithoutBom = new UTF8Encoding(false);
    private static int _nativeLibraryInitialized;

    private readonly BuddyDataPaths _paths;
    private readonly SqliteConnectionFactory _connections;
    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private bool _initialized;

    public BuddyDatabase(BuddyDataPaths paths, SqliteConnectionFactory connections)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _connections = connections ?? throw new ArgumentNullException(nameof(connections));
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized)
        {
            return;
        }

        await _initializationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initialized)
            {
                return;
            }

            InitializeNativeLibrary();
            Directory.CreateDirectory(_paths.Root);
            Directory.CreateDirectory(_paths.Backups);

            SchemaResetJournal? pendingReset = ReadResetJournal();
            if (pendingReset is not null)
            {
                await CompleteSchemaResetAsync(pendingReset, cancellationToken)
                    .ConfigureAwait(false);
                _initialized = true;
                return;
            }

            _paths.EnsureCreated();
            int schemaVersion;
            bool hasApplicationSchema;

            await using (SqliteConnection connection =
                         await _connections.OpenAsync(cancellationToken).ConfigureAwait(false))
            {
                await ExecutePragmasAsync(connection, cancellationToken).ConfigureAwait(false);
                schemaVersion = await GetSchemaVersionAsync(connection, cancellationToken)
                    .ConfigureAwait(false);

                if (schemaVersion == CurrentSchemaVersion)
                {
                    _initialized = true;
                    return;
                }

                hasApplicationSchema = await HasApplicationSchemaAsync(
                        connection,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (schemaVersion == 0 && !hasApplicationSchema)
                {
                    await CreateCurrentSchemaAsync(connection, cancellationToken)
                        .ConfigureAwait(false);
                    _initialized = true;
                    return;
                }

                await CheckpointAsync(connection, cancellationToken).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            SchemaResetJournal reset = StartSchemaReset(schemaVersion);
            await CompleteSchemaResetAsync(reset, cancellationToken).ConfigureAwait(false);
            _initialized = true;
        }
        finally
        {
            _initializationLock.Release();
        }
    }

    public void Dispose()
    {
        _initializationLock.Dispose();
        GC.SuppressFinalize(this);
    }

    private static void InitializeNativeLibrary()
    {
        if (Interlocked.Exchange(ref _nativeLibraryInitialized, 1) == 0)
        {
            SQLitePCL.Batteries_V2.Init();
        }
    }

    private static async Task ExecutePragmasAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        const string sql = """
            PRAGMA journal_mode = WAL;
            PRAGMA synchronous = NORMAL;
            PRAGMA foreign_keys = ON;
            PRAGMA busy_timeout = 5000;
            PRAGMA trusted_schema = OFF;
            """;

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<int> GetSchemaVersionAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        object? result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    private static async Task<bool> HasApplicationSchemaAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT EXISTS (
                SELECT 1
                FROM sqlite_schema
                WHERE name NOT LIKE 'sqlite_%'
                  AND type IN ('table', 'index', 'view', 'trigger')
            );
            """;
        object? result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt32(result, CultureInfo.InvariantCulture) != 0;
    }

    private static async Task CheckpointAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task CompleteSchemaResetAsync(
        SchemaResetJournal reset,
        CancellationToken cancellationToken)
    {
        SchemaResetJournal current = reset;
        if (string.Equals(current.Stage, ArchiveStage, StringComparison.Ordinal))
        {
            SqliteConnectionFactory.ClearPool();
            ArchiveApplicationState(current);
            current = current with { Stage = CreateStage };
            WriteResetJournal(current);
        }

        if (!string.Equals(current.Stage, CreateStage, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Buddy cannot resume local-state reset stage '{current.Stage}'.");
        }

        SqliteConnectionFactory.ClearPool();
        RemovePartialCurrentState();
        _paths.EnsureCreated();

        await using SqliteConnection connection =
            await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await ExecutePragmasAsync(connection, cancellationToken).ConfigureAwait(false);
        await CreateCurrentSchemaAsync(connection, cancellationToken).ConfigureAwait(false);

        File.Delete(GetResetMarkerPath());
    }

    private SchemaResetJournal StartSchemaReset(int previousSchemaVersion)
    {
        DateTimeOffset startedAt = DateTimeOffset.UtcNow;
        string backupDirectoryName = string.Create(
            CultureInfo.InvariantCulture,
            $"schema-{previousSchemaVersion}-to-{CurrentSchemaVersion}-{startedAt:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}");
        SchemaResetJournal reset = new(
            ArchiveStage,
            backupDirectoryName,
            previousSchemaVersion,
            startedAt);

        string backupDirectory = GetBackupDirectoryPath(backupDirectoryName);
        Directory.CreateDirectory(backupDirectory);
        string details = string.Create(
            CultureInfo.InvariantCulture,
            $"""
            Buddy local-state archive
            Previous schema: {previousSchemaVersion}
            Replacement schema: {CurrentSchemaVersion}
            Reset started UTC: {startedAt:O}
            Downloaded speech models, logs, and protected provider secrets were preserved in place.

            """);
        File.WriteAllText(
            Path.Combine(backupDirectory, "schema-reset.txt"),
            details,
            Utf8WithoutBom);
        WriteResetJournal(reset);
        return reset;
    }

    private void ArchiveApplicationState(SchemaResetJournal reset)
    {
        string backupDirectory = GetBackupDirectoryPath(reset.BackupDirectoryName);
        Directory.CreateDirectory(backupDirectory);

        MoveDirectoryIfNeeded(
            _paths.Recordings,
            Path.Combine(backupDirectory, Path.GetFileName(_paths.Recordings)));
        MoveDirectoryIfNeeded(
            _paths.CaptureJournals,
            Path.Combine(backupDirectory, Path.GetFileName(_paths.CaptureJournals)));
        MoveDirectoryIfNeeded(
            _paths.DialogWork,
            Path.Combine(backupDirectory, Path.GetFileName(_paths.DialogWork)));

        MoveFileIfNeeded(
            _paths.DatabasePath + "-wal",
            Path.Combine(backupDirectory, Path.GetFileName(_paths.DatabasePath) + "-wal"));
        MoveFileIfNeeded(
            _paths.DatabasePath + "-shm",
            Path.Combine(backupDirectory, Path.GetFileName(_paths.DatabasePath) + "-shm"));
        MoveFileIfNeeded(
            _paths.DatabasePath,
            Path.Combine(backupDirectory, Path.GetFileName(_paths.DatabasePath)));
    }

    private void RemovePartialCurrentState()
    {
        DeleteDirectoryIfPresent(_paths.Recordings);
        DeleteDirectoryIfPresent(_paths.CaptureJournals);
        DeleteDirectoryIfPresent(_paths.DialogWork);
        DeleteFileIfPresent(_paths.DatabasePath + "-wal");
        DeleteFileIfPresent(_paths.DatabasePath + "-shm");
        DeleteFileIfPresent(_paths.DatabasePath);
    }

    private SchemaResetJournal? ReadResetJournal()
    {
        string markerPath = GetResetMarkerPath();
        if (!File.Exists(markerPath))
        {
            return null;
        }

        string[] lines = File.ReadAllLines(markerPath, Utf8WithoutBom);
        if (lines.Length != 4
            || !int.TryParse(lines[2], NumberStyles.None, CultureInfo.InvariantCulture, out int previousVersion)
            || !DateTimeOffset.TryParseExact(
                lines[3],
                "O",
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out DateTimeOffset startedAt))
        {
            throw new InvalidOperationException(
                "Buddy cannot read its interrupted local-state reset journal.");
        }

        _ = GetBackupDirectoryPath(lines[1]);
        return new SchemaResetJournal(lines[0], lines[1], previousVersion, startedAt);
    }

    private void WriteResetJournal(SchemaResetJournal reset)
    {
        string markerPath = GetResetMarkerPath();
        string temporaryPath = markerPath + $".{Guid.NewGuid():N}.tmp";
        string contents = string.Join(
            Environment.NewLine,
            reset.Stage,
            reset.BackupDirectoryName,
            reset.PreviousSchemaVersion.ToString(CultureInfo.InvariantCulture),
            reset.StartedAtUtc.ToString("O", CultureInfo.InvariantCulture));

        try
        {
            File.WriteAllText(temporaryPath, contents, Utf8WithoutBom);
            File.Move(temporaryPath, markerPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private string GetResetMarkerPath()
    {
        return Path.Combine(_paths.Root, ResetMarkerFileName);
    }

    private string GetBackupDirectoryPath(string directoryName)
    {
        if (string.IsNullOrWhiteSpace(directoryName)
            || !string.Equals(
                Path.GetFileName(directoryName),
                directoryName,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Buddy's local-state reset journal contains an invalid backup directory.");
        }

        string backupRoot = Path.GetFullPath(_paths.Backups)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        string candidate = Path.GetFullPath(Path.Combine(backupRoot, directoryName));
        if (!candidate.StartsWith(backupRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Buddy's local-state reset journal points outside the backup directory.");
        }

        return candidate;
    }

    private static void MoveDirectoryIfNeeded(string source, string destination)
    {
        bool sourceExists = Directory.Exists(source);
        bool destinationExists = Directory.Exists(destination);
        if (sourceExists && destinationExists)
        {
            throw new InvalidOperationException(
                $"Buddy cannot archive '{Path.GetFileName(source)}' because both locations exist.");
        }

        if (sourceExists)
        {
            Directory.Move(source, destination);
        }
    }

    private static void MoveFileIfNeeded(string source, string destination)
    {
        bool sourceExists = File.Exists(source);
        bool destinationExists = File.Exists(destination);
        if (sourceExists && destinationExists)
        {
            throw new InvalidOperationException(
                $"Buddy cannot archive '{Path.GetFileName(source)}' because both locations exist.");
        }

        if (sourceExists)
        {
            File.Move(source, destination);
        }
    }

    private static void DeleteDirectoryIfPresent(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private static void DeleteFileIfPresent(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static async Task CreateCurrentSchemaAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using SqliteTransaction transaction =
            (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            CREATE TABLE recording (
                id TEXT NOT NULL PRIMARY KEY,
                kind INTEGER NOT NULL,
                created_at TEXT NOT NULL,
                capture_started_at TEXT NOT NULL,
                capture_ended_at TEXT NULL,
                wall_duration_ticks INTEGER NOT NULL,
                speech_duration_ticks INTEGER NOT NULL,
                input_device_id TEXT NULL,
                status INTEGER NOT NULL,
                display_title TEXT NOT NULL,
                generated_title TEXT NULL,
                last_error_code TEXT NULL,
                last_error_message TEXT NULL,
                deleted_at TEXT NULL,
                version INTEGER NOT NULL,
                CHECK (wall_duration_ticks >= 0),
                CHECK (speech_duration_ticks >= 0),
                CHECK (speech_duration_ticks <= wall_duration_ticks)
            );

            CREATE INDEX ix_recording_capture_started
                ON recording(capture_started_at DESC);
            CREATE INDEX ix_recording_kind_status
                ON recording(kind, status);

            CREATE TABLE audio_artifact (
                id TEXT NOT NULL PRIMARY KEY,
                recording_id TEXT NOT NULL,
                kind INTEGER NOT NULL,
                relative_path TEXT NOT NULL,
                container INTEGER NOT NULL,
                sample_rate INTEGER NOT NULL,
                channels INTEGER NOT NULL,
                duration_ticks INTEGER NOT NULL,
                byte_length INTEGER NOT NULL,
                sha256 TEXT NOT NULL,
                generator TEXT NULL,
                created_at TEXT NOT NULL,
                FOREIGN KEY (recording_id) REFERENCES recording(id) ON DELETE CASCADE,
                UNIQUE (recording_id, kind, relative_path)
            );

            CREATE INDEX ix_audio_artifact_recording
                ON audio_artifact(recording_id, kind);

            CREATE TABLE speech_segment (
                recording_id TEXT NOT NULL,
                sequence INTEGER NOT NULL,
                original_start_ticks INTEGER NOT NULL,
                original_end_ticks INTEGER NOT NULL,
                compact_start_ticks INTEGER NOT NULL,
                compact_end_ticks INTEGER NOT NULL,
                confidence REAL NOT NULL,
                PRIMARY KEY (recording_id, sequence),
                FOREIGN KEY (recording_id) REFERENCES recording(id) ON DELETE CASCADE
            );

            CREATE TABLE transcript_revision (
                id TEXT NOT NULL PRIMARY KEY,
                recording_id TEXT NOT NULL,
                parent_revision_id TEXT NULL,
                kind INTEGER NOT NULL,
                text TEXT NOT NULL,
                content_sha256 TEXT NOT NULL,
                created_at TEXT NOT NULL,
                provider TEXT NULL,
                model TEXT NULL,
                schema_version TEXT NULL,
                is_current INTEGER NOT NULL,
                FOREIGN KEY (recording_id) REFERENCES recording(id) ON DELETE CASCADE,
                FOREIGN KEY (parent_revision_id) REFERENCES transcript_revision(id)
            );

            CREATE INDEX ix_transcript_revision_recording_created
                ON transcript_revision(recording_id, created_at);
            CREATE UNIQUE INDEX ux_transcript_revision_current
                ON transcript_revision(recording_id)
                WHERE is_current = 1;

            CREATE TABLE background_job (
                id TEXT NOT NULL PRIMARY KEY,
                recording_id TEXT NULL,
                type INTEGER NOT NULL,
                payload_json TEXT NOT NULL,
                state INTEGER NOT NULL,
                attempt_count INTEGER NOT NULL,
                created_at TEXT NOT NULL,
                available_at TEXT NOT NULL,
                lease_expires_at TEXT NULL,
                lease_owner TEXT NULL,
                last_error_code TEXT NULL,
                last_error_message TEXT NULL,
                FOREIGN KEY (recording_id) REFERENCES recording(id) ON DELETE CASCADE
            );

            CREATE INDEX ix_background_job_available
                ON background_job(state, available_at, created_at);
            CREATE UNIQUE INDEX ux_background_job_active_stage
                ON background_job(recording_id, type)
                WHERE state IN (0, 1);

            CREATE TABLE app_setting (
                key TEXT NOT NULL PRIMARY KEY,
                value_json TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );

            CREATE TABLE dialog_session (
                id TEXT NOT NULL PRIMARY KEY,
                recording_id TEXT NOT NULL UNIQUE,
                status INTEGER NOT NULL,
                started_at TEXT NOT NULL,
                ended_at TEXT NULL,
                system_instruction TEXT NOT NULL,
                provider TEXT NULL,
                model TEXT NULL,
                last_error TEXT NULL,
                version INTEGER NOT NULL,
                FOREIGN KEY (recording_id) REFERENCES recording(id) ON DELETE CASCADE
            );

            CREATE INDEX ix_dialog_session_started
                ON dialog_session(started_at DESC);
            CREATE UNIQUE INDEX ux_dialog_session_one_active
                ON dialog_session((1))
                WHERE status IN (0, 1);

            CREATE TABLE dialog_message (
                id TEXT NOT NULL PRIMARY KEY,
                session_id TEXT NOT NULL,
                sequence INTEGER NOT NULL,
                role INTEGER NOT NULL,
                text TEXT NOT NULL,
                created_at TEXT NOT NULL,
                provider TEXT NULL,
                model TEXT NULL,
                latency_ticks INTEGER NULL,
                prompt_tokens INTEGER NULL,
                completion_tokens INTEGER NULL,
                audio_artifact_id TEXT NULL,
                FOREIGN KEY (session_id) REFERENCES dialog_session(id) ON DELETE CASCADE,
                FOREIGN KEY (audio_artifact_id) REFERENCES audio_artifact(id) ON DELETE SET NULL,
                UNIQUE (session_id, sequence)
            );

            CREATE INDEX ix_dialog_message_session_sequence
                ON dialog_message(session_id, sequence);

            CREATE TABLE pronunciation_assessment (
                recording_id TEXT NOT NULL PRIMARY KEY,
                transcript TEXT NOT NULL,
                created_at TEXT NOT NULL,
                model TEXT NOT NULL,
                schema_version TEXT NOT NULL,
                phonetic_transcript TEXT NOT NULL DEFAULT '',
                FOREIGN KEY (recording_id) REFERENCES recording(id) ON DELETE CASCADE
            );

            CREATE TABLE pronunciation_word (
                recording_id TEXT NOT NULL,
                sequence INTEGER NOT NULL,
                text TEXT NOT NULL,
                start_ticks INTEGER NOT NULL,
                end_ticks INTEGER NOT NULL,
                confidence REAL NOT NULL,
                PRIMARY KEY (recording_id, sequence),
                FOREIGN KEY (recording_id)
                    REFERENCES pronunciation_assessment(recording_id)
                    ON DELETE CASCADE,
                CHECK (sequence >= 0),
                CHECK (start_ticks >= 0),
                CHECK (end_ticks >= start_ticks),
                CHECK (confidence >= 0 AND confidence <= 1)
            );

            CREATE TABLE dialog_pronunciation_assessment (
                message_id TEXT NOT NULL PRIMARY KEY,
                transcript TEXT NOT NULL,
                phonetic_transcript TEXT NOT NULL,
                created_at TEXT NOT NULL,
                model TEXT NOT NULL,
                schema_version TEXT NOT NULL,
                FOREIGN KEY (message_id)
                    REFERENCES dialog_message(id)
                    ON DELETE CASCADE
            );

            CREATE TABLE dialog_pronunciation_word (
                message_id TEXT NOT NULL,
                sequence INTEGER NOT NULL,
                text TEXT NOT NULL,
                start_ticks INTEGER NOT NULL,
                end_ticks INTEGER NOT NULL,
                confidence REAL NOT NULL,
                PRIMARY KEY (message_id, sequence),
                FOREIGN KEY (message_id)
                    REFERENCES dialog_pronunciation_assessment(message_id)
                    ON DELETE CASCADE,
                CHECK (sequence >= 0),
                CHECK (start_ticks >= 0),
                CHECK (end_ticks >= start_ticks),
                CHECK (confidence >= 0 AND confidence <= 1)
            );

            CREATE TABLE audio_waveform (
                artifact_id TEXT NOT NULL PRIMARY KEY,
                duration_ticks INTEGER NOT NULL,
                peaks BLOB NOT NULL,
                sample_count INTEGER NOT NULL,
                created_at TEXT NOT NULL,
                schema_version TEXT NOT NULL,
                FOREIGN KEY (artifact_id)
                    REFERENCES audio_artifact(id)
                    ON DELETE CASCADE,
                CHECK (duration_ticks >= 0),
                CHECK (sample_count BETWEEN 16 AND 512),
                CHECK (length(peaks) = sample_count)
            );

            PRAGMA user_version = {CurrentSchemaVersion};
            """;

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private sealed record SchemaResetJournal(
        string Stage,
        string BackupDirectoryName,
        int PreviousSchemaVersion,
        DateTimeOffset StartedAtUtc);
}
