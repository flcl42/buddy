using System.Globalization;
using System.Text;
using Buddy.Core.Domain;
using Microsoft.Data.Sqlite;

namespace Buddy.Persistence.Tests;

public sealed class BuddyDatabaseTests
{
    [Fact]
    public async Task InitializeAsyncCreatesCurrentSchemaAndIsIdempotent()
    {
        await using TemporaryBuddyStore store = await TemporaryBuddyStore.CreateAsync();

        await store.Database.InitializeAsync();
        await using SqliteConnection connection = await store.Connections.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                (SELECT user_version FROM pragma_user_version),
                (SELECT COUNT(*) FROM sqlite_schema
                 WHERE type = 'table' AND name NOT LIKE 'sqlite_%'),
                (SELECT COUNT(*) FROM pragma_table_info('pronunciation_assessment')
                 WHERE name = 'phonetic_transcript'),
                (SELECT COUNT(*) FROM sqlite_schema
                 WHERE type = 'index' AND name = 'ux_background_job_active_stage');
            """;

        await using SqliteDataReader reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(5L, reader.GetInt64(0));
        Assert.Equal(13L, reader.GetInt64(1));
        Assert.Equal(1L, reader.GetInt64(2));
        Assert.Equal(1L, reader.GetInt64(3));
        Assert.True(File.Exists(store.Paths.DatabasePath));
        Assert.True(Directory.Exists(store.Paths.Recordings));
        Assert.True(Directory.Exists(store.Paths.Models));
        Assert.True(Directory.Exists(store.Paths.CaptureJournals));
        Assert.True(Directory.Exists(store.Paths.DialogWork));
        Assert.True(Directory.Exists(store.Paths.SpeechCache));
        Assert.True(Directory.Exists(store.Paths.Logs));
        Assert.True(Directory.Exists(store.Paths.Backups));
        Assert.Empty(Directory.EnumerateFileSystemEntries(store.Paths.Backups));
    }

    [Fact]
    public async Task MatchingCurrentSchemaLeavesApplicationStateUntouched()
    {
        await using TemporaryBuddyStore store = await TemporaryBuddyStore.CreateAsync();
        SqliteRecordingRepository recordings = new(store.Connections);
        Recording existing = Recording.Start(
            RecordingKind.Trainer,
            new DateTimeOffset(2026, 8, 2, 9, 0, 0, TimeSpan.Zero),
            id: Guid.NewGuid());
        await recordings.AddAsync(existing);
        string recordingMarker = Path.Combine(store.Paths.Recordings, "keep-audio.pcm");
        await File.WriteAllTextAsync(recordingMarker, "recording");

        using BuddyDatabase reopened = new(store.Paths, store.Connections);
        await reopened.InitializeAsync();

        Assert.Equal(existing, await recordings.GetAsync(existing.Id));
        Assert.True(File.Exists(recordingMarker));
        Assert.Empty(Directory.EnumerateFileSystemEntries(store.Paths.Backups));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(6)]
    public async Task SchemaMismatchResetsAndArchivesApplicationState(
        int storedSchemaVersion)
    {
        await using TemporaryBuddyStore store = await TemporaryBuddyStore.CreateAsync();
        SqliteRecordingRepository recordings = new(store.Connections);
        Recording existing = Recording.Start(
            RecordingKind.Meeting,
            new DateTimeOffset(2026, 8, 2, 9, 30, 0, TimeSpan.Zero),
            id: Guid.NewGuid());
        await recordings.AddAsync(existing);

        string recordingMarker = Path.Combine(store.Paths.Recordings, "old-audio.pcm");
        string captureMarker = Path.Combine(store.Paths.CaptureJournals, "old-capture.json");
        string dialogMarker = Path.Combine(store.Paths.DialogWork, "old-dialog.pcm");
        string modelMarker = Path.Combine(store.Paths.Models, "keep-model.bin");
        string logMarker = Path.Combine(store.Paths.Logs, "keep.log");
        string secrets = Path.Combine(store.Paths.Root, "secrets");
        string secretMarker = Path.Combine(secrets, "keep.secret");
        Directory.CreateDirectory(secrets);
        await File.WriteAllTextAsync(recordingMarker, "recording");
        await File.WriteAllTextAsync(captureMarker, "capture");
        await File.WriteAllTextAsync(dialogMarker, "dialog");
        await File.WriteAllTextAsync(modelMarker, "model");
        await File.WriteAllTextAsync(logMarker, "log");
        await File.WriteAllTextAsync(secretMarker, "secret");

        await SetSchemaVersionAsync(store.Connections, storedSchemaVersion);

        using BuddyDatabase reset = new(store.Paths, store.Connections);
        await reset.InitializeAsync();

        Assert.Null(await recordings.GetAsync(existing.Id));
        Assert.False(File.Exists(recordingMarker));
        Assert.False(File.Exists(captureMarker));
        Assert.False(File.Exists(dialogMarker));
        Assert.True(File.Exists(modelMarker));
        Assert.True(File.Exists(logMarker));
        Assert.True(File.Exists(secretMarker));
        Assert.False(File.Exists(Path.Combine(store.Paths.Root, ".schema-reset")));

        string backup = Assert.Single(Directory.GetDirectories(store.Paths.Backups));
        Assert.StartsWith(
            $"schema-{storedSchemaVersion}-to-5-",
            Path.GetFileName(backup),
            StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(backup, "buddy.db")));
        Assert.True(File.Exists(Path.Combine(backup, "schema-reset.txt")));
        Assert.True(File.Exists(Path.Combine(backup, "recordings", "old-audio.pcm")));
        Assert.True(File.Exists(Path.Combine(backup, "capture-journal", "old-capture.json")));
        Assert.True(File.Exists(Path.Combine(backup, "dialog-work", "old-dialog.pcm")));

        await AssertCurrentEmptyDatabaseAsync(store.Connections);
    }

    [Fact]
    public async Task InterruptedArchiveStageResumesBeforeCreatingCurrentSchema()
    {
        await using TemporaryBuddyStore store = await TemporaryBuddyStore.CreateAsync();
        await SetSchemaVersionAsync(store.Connections, 4);
        SqliteConnectionFactory.ClearPool();

        string backupName = "schema-4-to-5-interrupted-test";
        string backup = Path.Combine(store.Paths.Backups, backupName);
        Directory.CreateDirectory(backup);
        string recordingMarker = Path.Combine(store.Paths.Recordings, "before-crash.pcm");
        await File.WriteAllTextAsync(recordingMarker, "recording");
        Directory.Move(store.Paths.Recordings, Path.Combine(backup, "recordings"));

        string marker = Path.Combine(store.Paths.Root, ".schema-reset");
        await File.WriteAllLinesAsync(
            marker,
            [
                "archive",
                backupName,
                "4",
                new DateTimeOffset(2026, 8, 2, 10, 0, 0, TimeSpan.Zero)
                    .ToString("O", CultureInfo.InvariantCulture),
            ],
            new UTF8Encoding(false));

        using BuddyDatabase resumed = new(store.Paths, store.Connections);
        await resumed.InitializeAsync();

        Assert.False(File.Exists(marker));
        Assert.True(File.Exists(Path.Combine(backup, "recordings", "before-crash.pcm")));
        Assert.True(File.Exists(Path.Combine(backup, "buddy.db")));
        Assert.True(Directory.Exists(store.Paths.Recordings));
        await AssertCurrentEmptyDatabaseAsync(store.Connections);
    }

    [Fact]
    public async Task DatabaseUsesWalModeAndForeignKeys()
    {
        await using TemporaryBuddyStore store = await TemporaryBuddyStore.CreateAsync();
        await using SqliteConnection connection = await store.Connections.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode; PRAGMA foreign_keys;";

        await using SqliteDataReader reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("wal", reader.GetString(0));

        Assert.True(await reader.NextResultAsync());
        Assert.True(await reader.ReadAsync());
        Assert.Equal(1L, reader.GetInt64(0));
    }

    [Fact]
    public void PathsRejectTraversalOutsideRecordingRoot()
    {
        BuddyDataPaths paths = new(Path.Combine(
            Path.GetTempPath(),
            "buddy-tests",
            Guid.NewGuid().ToString("N")));

        Assert.Throws<InvalidOperationException>(
            () => paths.ResolveRecordingArtifact(Path.Combine("..", "private.txt")));
    }

    private static async Task SetSchemaVersionAsync(
        SqliteConnectionFactory connections,
        int schemaVersion)
    {
        await using SqliteConnection connection = await connections.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = string.Create(
            CultureInfo.InvariantCulture,
            $"PRAGMA user_version = {schemaVersion};");
        await command.ExecuteNonQueryAsync();
    }

    private static async Task AssertCurrentEmptyDatabaseAsync(
        SqliteConnectionFactory connections)
    {
        await using SqliteConnection connection = await connections.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                (SELECT user_version FROM pragma_user_version),
                (SELECT COUNT(*) FROM recording),
                (SELECT COUNT(*) FROM pragma_foreign_key_check),
                (SELECT integrity_check FROM pragma_integrity_check);
            """;
        await using SqliteDataReader reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(5L, reader.GetInt64(0));
        Assert.Equal(0L, reader.GetInt64(1));
        Assert.Equal(0L, reader.GetInt64(2));
        Assert.Equal("ok", reader.GetString(3));
    }
}
