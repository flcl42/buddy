using Buddy.Core.Abstractions;
using Buddy.Core.Domain;
using Microsoft.Data.Sqlite;

namespace Buddy.Persistence;

public sealed class SqliteRecordingRepository : IRecordingRepository
{
    private readonly SqliteConnectionFactory _connections;

    public SqliteRecordingRepository(SqliteConnectionFactory connections)
    {
        _connections = connections ?? throw new ArgumentNullException(nameof(connections));
    }

    public async Task AddAsync(Recording recording, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recording);

        await using SqliteConnection connection =
            await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO recording (
                id, kind, created_at, capture_started_at, capture_ended_at,
                wall_duration_ticks, speech_duration_ticks, input_device_id,
                status, display_title, generated_title, last_error_code,
                last_error_message, deleted_at, version)
            VALUES (
                $id, $kind, $created_at, $capture_started_at, $capture_ended_at,
                $wall_duration_ticks, $speech_duration_ticks, $input_device_id,
                $status, $display_title, $generated_title, $last_error_code,
                $last_error_message, $deleted_at, $version);
            """;
        AddRecordingParameters(command, recording);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<Recording?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection =
            await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"{RecordingSelect} WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id.ToString("D"));

        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? ReadRecording(reader)
            : null;
    }

    public async Task<IReadOnlyList<Recording>> ListAsync(
        RecordingQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        query.Validate();

        await using SqliteConnection connection =
            await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"""
            {RecordingSelect}
            WHERE ($include_deleted = 1 OR status <> $deleted_status)
              AND ($kind IS NULL OR kind = $kind)
              AND ($status IS NULL OR status = $status)
              AND (
                    $search IS NULL
                    OR instr(lower(display_title), lower($search)) > 0
                    OR EXISTS (
                        SELECT 1
                        FROM transcript_revision tr
                        WHERE tr.recording_id = recording.id
                          AND instr(lower(tr.text), lower($search)) > 0
                    )
                    OR EXISTS (
                        SELECT 1
                        FROM dialog_session ds
                        INNER JOIN dialog_message dm
                            ON dm.session_id = ds.id
                        WHERE ds.recording_id = recording.id
                          AND instr(lower(dm.text), lower($search)) > 0
                    )
                  )
            ORDER BY capture_started_at DESC
            LIMIT $limit OFFSET $offset;
            """;
        command.Parameters.AddWithValue("$include_deleted", query.IncludeDeleted ? 1 : 0);
        command.Parameters.AddWithValue("$deleted_status", (int)RecordingStatus.Deleted);
        command.Parameters.AddWithValue("$kind", query.Kind.HasValue ? (int)query.Kind.Value : DBNull.Value);
        command.Parameters.AddWithValue("$status", query.Status.HasValue ? (int)query.Status.Value : DBNull.Value);
        command.Parameters.AddWithValue(
            "$search",
            string.IsNullOrWhiteSpace(query.Search) ? DBNull.Value : query.Search.Trim());
        command.Parameters.AddWithValue("$limit", query.Limit);
        command.Parameters.AddWithValue("$offset", query.Offset);

        List<Recording> recordings = [];
        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            recordings.Add(ReadRecording(reader));
        }

        return recordings;
    }

    public async Task<bool> TryUpdateAsync(
        Recording recording,
        long expectedVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recording);
        ArgumentOutOfRangeException.ThrowIfNegative(expectedVersion);

        await using SqliteConnection connection =
            await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            UPDATE recording
            SET kind = $kind,
                created_at = $created_at,
                capture_started_at = $capture_started_at,
                capture_ended_at = $capture_ended_at,
                wall_duration_ticks = $wall_duration_ticks,
                speech_duration_ticks = $speech_duration_ticks,
                input_device_id = $input_device_id,
                status = $status,
                display_title = $display_title,
                generated_title = $generated_title,
                last_error_code = $last_error_code,
                last_error_message = $last_error_message,
                deleted_at = $deleted_at,
                version = $version
            WHERE id = $id AND version = $expected_version;
            """;
        AddRecordingParameters(command, recording);
        command.Parameters.AddWithValue("$expected_version", expectedVersion);

        int affected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return affected == 1;
    }

    public async Task AddAudioArtifactAsync(
        AudioArtifact artifact,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(artifact);

        await using SqliteConnection connection =
            await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO audio_artifact (
                id, recording_id, kind, relative_path, container, sample_rate,
                channels, duration_ticks, byte_length, sha256, generator, created_at)
            VALUES (
                $id, $recording_id, $kind, $relative_path, $container, $sample_rate,
                $channels, $duration_ticks, $byte_length, $sha256, $generator, $created_at);
            """;
        command.Parameters.AddWithValue("$id", artifact.Id.ToString("D"));
        command.Parameters.AddWithValue("$recording_id", artifact.RecordingId.ToString("D"));
        command.Parameters.AddWithValue("$kind", (int)artifact.Kind);
        command.Parameters.AddWithValue("$relative_path", artifact.RelativePath);
        command.Parameters.AddWithValue("$container", (int)artifact.Container);
        command.Parameters.AddWithValue("$sample_rate", artifact.SampleRate);
        command.Parameters.AddWithValue("$channels", artifact.Channels);
        command.Parameters.AddWithValue("$duration_ticks", artifact.Duration.Ticks);
        command.Parameters.AddWithValue("$byte_length", artifact.ByteLength);
        command.Parameters.AddWithValue("$sha256", artifact.Sha256);
        command.Parameters.AddWithValue("$generator", (object?)artifact.Generator ?? DBNull.Value);
        command.Parameters.AddWithValue("$created_at", SqliteValue.Date(artifact.CreatedAt));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> UpdateAudioArtifactAsync(
        AudioArtifact artifact,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(artifact);

        await using SqliteConnection connection =
            await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            UPDATE audio_artifact
            SET relative_path = $relative_path,
                container = $container,
                sample_rate = $sample_rate,
                channels = $channels,
                duration_ticks = $duration_ticks,
                byte_length = $byte_length,
                sha256 = $sha256,
                generator = $generator,
                created_at = $created_at
            WHERE id = $id AND recording_id = $recording_id;
            """;
        command.Parameters.AddWithValue("$id", artifact.Id.ToString("D"));
        command.Parameters.AddWithValue(
            "$recording_id",
            artifact.RecordingId.ToString("D"));
        command.Parameters.AddWithValue("$relative_path", artifact.RelativePath);
        command.Parameters.AddWithValue("$container", (int)artifact.Container);
        command.Parameters.AddWithValue("$sample_rate", artifact.SampleRate);
        command.Parameters.AddWithValue("$channels", artifact.Channels);
        command.Parameters.AddWithValue("$duration_ticks", artifact.Duration.Ticks);
        command.Parameters.AddWithValue("$byte_length", artifact.ByteLength);
        command.Parameters.AddWithValue("$sha256", artifact.Sha256);
        command.Parameters.AddWithValue(
            "$generator",
            (object?)artifact.Generator ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$created_at",
            SqliteValue.Date(artifact.CreatedAt));
        int affected = await command
            .ExecuteNonQueryAsync(cancellationToken)
            .ConfigureAwait(false);
        return affected == 1;
    }

    public async Task<IReadOnlyList<AudioArtifact>> GetAudioArtifactsAsync(
        Guid recordingId,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection =
            await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, recording_id, kind, relative_path, container, sample_rate,
                   channels, duration_ticks, byte_length, sha256, generator, created_at
            FROM audio_artifact
            WHERE recording_id = $recording_id
            ORDER BY kind, created_at;
            """;
        command.Parameters.AddWithValue("$recording_id", recordingId.ToString("D"));

        List<AudioArtifact> artifacts = [];
        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            artifacts.Add(new AudioArtifact(
                reader.GetGuid(0),
                reader.GetGuid(1),
                (AudioArtifactKind)reader.GetInt32(2),
                reader.GetString(3),
                (AudioContainer)reader.GetInt32(4),
                reader.GetInt32(5),
                reader.GetInt32(6),
                TimeSpan.FromTicks(reader.GetInt64(7)),
                reader.GetInt64(8),
                reader.GetString(9),
                SqliteValue.ReadNullableString(reader, 10),
                SqliteValue.ReadDate(reader, 11)));
        }

        return artifacts;
    }

    public async Task ReplaceSpeechSegmentsAsync(
        Guid recordingId,
        IReadOnlyList<SpeechSegment> segments,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(segments);

        await using SqliteConnection connection =
            await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction =
            (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await using (SqliteCommand delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM speech_segment WHERE recording_id = $recording_id;";
            delete.Parameters.AddWithValue("$recording_id", recordingId.ToString("D"));
            await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        foreach (SpeechSegment segment in segments)
        {
            if (segment.RecordingId != recordingId)
            {
                throw new ArgumentException("Every segment must belong to the requested recording.", nameof(segments));
            }

            await using SqliteCommand insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO speech_segment (
                    recording_id, sequence, original_start_ticks, original_end_ticks,
                    compact_start_ticks, compact_end_ticks, confidence)
                VALUES (
                    $recording_id, $sequence, $original_start_ticks, $original_end_ticks,
                    $compact_start_ticks, $compact_end_ticks, $confidence);
                """;
            insert.Parameters.AddWithValue("$recording_id", recordingId.ToString("D"));
            insert.Parameters.AddWithValue("$sequence", segment.Sequence);
            insert.Parameters.AddWithValue("$original_start_ticks", segment.OriginalStart.Ticks);
            insert.Parameters.AddWithValue("$original_end_ticks", segment.OriginalEnd.Ticks);
            insert.Parameters.AddWithValue("$compact_start_ticks", segment.CompactStart.Ticks);
            insert.Parameters.AddWithValue("$compact_end_ticks", segment.CompactEnd.Ticks);
            insert.Parameters.AddWithValue("$confidence", segment.Confidence);
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<SpeechSegment>> GetSpeechSegmentsAsync(
        Guid recordingId,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection =
            await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT recording_id, sequence, original_start_ticks, original_end_ticks,
                   compact_start_ticks, compact_end_ticks, confidence
            FROM speech_segment
            WHERE recording_id = $recording_id
            ORDER BY sequence;
            """;
        command.Parameters.AddWithValue("$recording_id", recordingId.ToString("D"));

        List<SpeechSegment> segments = [];
        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            segments.Add(new SpeechSegment(
                reader.GetGuid(0),
                reader.GetInt32(1),
                TimeSpan.FromTicks(reader.GetInt64(2)),
                TimeSpan.FromTicks(reader.GetInt64(3)),
                TimeSpan.FromTicks(reader.GetInt64(4)),
                TimeSpan.FromTicks(reader.GetInt64(5)),
                reader.GetFloat(6)));
        }

        return segments;
    }

    public async Task AddTranscriptRevisionAsync(
        TranscriptRevision revision,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(revision);

        await using SqliteConnection connection =
            await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction =
            (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        if (revision.IsCurrent)
        {
            await using SqliteCommand reset = connection.CreateCommand();
            reset.Transaction = transaction;
            reset.CommandText = """
                UPDATE transcript_revision
                SET is_current = 0
                WHERE recording_id = $recording_id AND is_current = 1;
                """;
            reset.Parameters.AddWithValue("$recording_id", revision.RecordingId.ToString("D"));
            await reset.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using SqliteCommand insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO transcript_revision (
                id, recording_id, parent_revision_id, kind, text, content_sha256,
                created_at, provider, model, schema_version, is_current)
            VALUES (
                $id, $recording_id, $parent_revision_id, $kind, $text, $content_sha256,
                $created_at, $provider, $model, $schema_version, $is_current);
            """;
        insert.Parameters.AddWithValue("$id", revision.Id.ToString("D"));
        insert.Parameters.AddWithValue("$recording_id", revision.RecordingId.ToString("D"));
        insert.Parameters.AddWithValue(
            "$parent_revision_id",
            revision.ParentRevisionId.HasValue ? revision.ParentRevisionId.Value.ToString("D") : DBNull.Value);
        insert.Parameters.AddWithValue("$kind", (int)revision.Kind);
        insert.Parameters.AddWithValue("$text", revision.Text);
        insert.Parameters.AddWithValue("$content_sha256", revision.ContentSha256);
        insert.Parameters.AddWithValue("$created_at", SqliteValue.Date(revision.CreatedAt));
        insert.Parameters.AddWithValue("$provider", (object?)revision.Provider ?? DBNull.Value);
        insert.Parameters.AddWithValue("$model", (object?)revision.Model ?? DBNull.Value);
        insert.Parameters.AddWithValue("$schema_version", (object?)revision.SchemaVersion ?? DBNull.Value);
        insert.Parameters.AddWithValue("$is_current", revision.IsCurrent ? 1 : 0);
        await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<TranscriptRevision>> GetTranscriptRevisionsAsync(
        Guid recordingId,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection =
            await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, recording_id, parent_revision_id, kind, text, content_sha256,
                   created_at, provider, model, schema_version, is_current
            FROM transcript_revision
            WHERE recording_id = $recording_id
            ORDER BY created_at, rowid;
            """;
        command.Parameters.AddWithValue("$recording_id", recordingId.ToString("D"));

        List<TranscriptRevision> revisions = [];
        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            revisions.Add(new TranscriptRevision(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.IsDBNull(2) ? null : reader.GetGuid(2),
                (TranscriptRevisionKind)reader.GetInt32(3),
                reader.GetString(4),
                reader.GetString(5),
                SqliteValue.ReadDate(reader, 6),
                SqliteValue.ReadNullableString(reader, 7),
                SqliteValue.ReadNullableString(reader, 8),
                SqliteValue.ReadNullableString(reader, 9),
                reader.GetBoolean(10)));
        }

        return revisions;
    }

    public async Task ReplacePronunciationAssessmentAsync(
        Guid recordingId,
        PronunciationAssessment? assessment,
        CancellationToken cancellationToken = default)
    {
        if (assessment is not null)
        {
            if (assessment.RecordingId != recordingId)
            {
                throw new ArgumentException(
                    "The assessment must belong to the requested recording.",
                    nameof(assessment));
            }

            for (int index = 0; index < assessment.Words.Count; index++)
            {
                PronunciationWord word = assessment.Words[index];
                if (word.SourceId != recordingId || word.Sequence != index)
                {
                    throw new ArgumentException(
                        "Pronunciation words must belong to the requested recording and have contiguous sequence numbers.",
                        nameof(assessment));
                }
            }
        }

        await using SqliteConnection connection =
            await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction =
            (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await using (SqliteCommand delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText =
                "DELETE FROM pronunciation_assessment WHERE recording_id = $recording_id;";
            delete.Parameters.AddWithValue("$recording_id", recordingId.ToString("D"));
            await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        if (assessment is null)
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        await using (SqliteCommand insertAssessment = connection.CreateCommand())
        {
            insertAssessment.Transaction = transaction;
            insertAssessment.CommandText = """
                INSERT INTO pronunciation_assessment (
                    recording_id, transcript, phonetic_transcript, created_at,
                    model, schema_version)
                VALUES (
                    $recording_id, $transcript, $phonetic_transcript, $created_at,
                    $model, $schema_version);
                """;
            insertAssessment.Parameters.AddWithValue("$recording_id", recordingId.ToString("D"));
            insertAssessment.Parameters.AddWithValue("$transcript", assessment.Transcript);
            insertAssessment.Parameters.AddWithValue(
                "$phonetic_transcript",
                assessment.PhoneticTranscript);
            insertAssessment.Parameters.AddWithValue("$created_at", SqliteValue.Date(assessment.CreatedAt));
            insertAssessment.Parameters.AddWithValue("$model", assessment.Model);
            insertAssessment.Parameters.AddWithValue("$schema_version", assessment.SchemaVersion);
            await insertAssessment.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        foreach (PronunciationWord word in assessment.Words)
        {
            await using SqliteCommand insertWord = connection.CreateCommand();
            insertWord.Transaction = transaction;
            insertWord.CommandText = """
                INSERT INTO pronunciation_word (
                    recording_id, sequence, text, start_ticks, end_ticks, confidence)
                VALUES (
                    $recording_id, $sequence, $text, $start_ticks, $end_ticks, $confidence);
                """;
            insertWord.Parameters.AddWithValue("$recording_id", recordingId.ToString("D"));
            insertWord.Parameters.AddWithValue("$sequence", word.Sequence);
            insertWord.Parameters.AddWithValue("$text", word.Text);
            insertWord.Parameters.AddWithValue("$start_ticks", word.Start.Ticks);
            insertWord.Parameters.AddWithValue("$end_ticks", word.End.Ticks);
            insertWord.Parameters.AddWithValue("$confidence", word.Confidence);
            await insertWord.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<PronunciationAssessment?> GetPronunciationAssessmentAsync(
        Guid recordingId,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection =
            await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);

        string? transcript;
        string? phoneticTranscript;
        DateTimeOffset createdAt;
        string? model;
        string? schemaVersion;
        await using (SqliteCommand assessmentCommand = connection.CreateCommand())
        {
            assessmentCommand.CommandText = """
                SELECT transcript, phonetic_transcript, created_at, model, schema_version
                FROM pronunciation_assessment
                WHERE recording_id = $recording_id;
                """;
            assessmentCommand.Parameters.AddWithValue("$recording_id", recordingId.ToString("D"));
            await using SqliteDataReader reader =
                await assessmentCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            transcript = reader.GetString(0);
            phoneticTranscript = reader.GetString(1);
            createdAt = SqliteValue.ReadDate(reader, 2);
            model = reader.GetString(3);
            schemaVersion = reader.GetString(4);
        }

        List<PronunciationWord> words = [];
        await using (SqliteCommand wordsCommand = connection.CreateCommand())
        {
            wordsCommand.CommandText = """
                SELECT sequence, text, start_ticks, end_ticks, confidence
                FROM pronunciation_word
                WHERE recording_id = $recording_id
                ORDER BY sequence;
                """;
            wordsCommand.Parameters.AddWithValue("$recording_id", recordingId.ToString("D"));
            await using SqliteDataReader reader =
                await wordsCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                words.Add(new PronunciationWord(
                    recordingId,
                    reader.GetInt32(0),
                    reader.GetString(1),
                    TimeSpan.FromTicks(reader.GetInt64(2)),
                    TimeSpan.FromTicks(reader.GetInt64(3)),
                    reader.GetFloat(4)));
            }
        }

        return new PronunciationAssessment(
            recordingId,
            transcript,
            phoneticTranscript,
            createdAt,
            model,
            schemaVersion,
            words);
    }

    public async Task ReplaceAudioWaveformAsync(
        AudioWaveform waveform,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(waveform);
        if (waveform.ArtifactId == Guid.Empty)
        {
            throw new ArgumentException(
                "A waveform must belong to an audio artifact.",
                nameof(waveform));
        }

        if (waveform.Duration < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(waveform),
                "Waveform duration cannot be negative.");
        }

        if (waveform.Peaks.Count is < 16 or > 512)
        {
            throw new ArgumentOutOfRangeException(
                nameof(waveform),
                "A waveform must contain between 16 and 512 samples.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(waveform.SchemaVersion);
        byte[] peaks = waveform.Peaks.ToArray();
        await using SqliteConnection connection =
            await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO audio_waveform (
                artifact_id, duration_ticks, peaks, sample_count,
                created_at, schema_version)
            VALUES (
                $artifact_id, $duration_ticks, $peaks, $sample_count,
                $created_at, $schema_version)
            ON CONFLICT(artifact_id) DO UPDATE SET
                duration_ticks = excluded.duration_ticks,
                peaks = excluded.peaks,
                sample_count = excluded.sample_count,
                created_at = excluded.created_at,
                schema_version = excluded.schema_version;
            """;
        command.Parameters.AddWithValue(
            "$artifact_id",
            waveform.ArtifactId.ToString("D"));
        command.Parameters.AddWithValue("$duration_ticks", waveform.Duration.Ticks);
        command.Parameters.AddWithValue("$peaks", peaks);
        command.Parameters.AddWithValue("$sample_count", peaks.Length);
        command.Parameters.AddWithValue(
            "$created_at",
            SqliteValue.Date(waveform.CreatedAt));
        command.Parameters.AddWithValue("$schema_version", waveform.SchemaVersion);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<AudioWaveform?> GetAudioWaveformAsync(
        Guid artifactId,
        CancellationToken cancellationToken = default)
    {
        if (artifactId == Guid.Empty)
        {
            throw new ArgumentException(
                "An audio artifact identifier is required.",
                nameof(artifactId));
        }

        await using SqliteConnection connection =
            await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT duration_ticks, peaks, sample_count, created_at, schema_version
            FROM audio_waveform
            WHERE artifact_id = $artifact_id;
            """;
        command.Parameters.AddWithValue("$artifact_id", artifactId.ToString("D"));
        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        byte[] peaks = (byte[])reader.GetValue(1);
        int sampleCount = reader.GetInt32(2);
        if (peaks.Length != sampleCount)
        {
            throw new InvalidDataException(
                $"Waveform {artifactId:D} has an invalid stored sample count.");
        }

        return new AudioWaveform(
            artifactId,
            TimeSpan.FromTicks(reader.GetInt64(0)),
            peaks,
            SqliteValue.ReadDate(reader, 3),
            reader.GetString(4));
    }

    private const string RecordingSelect = """
        SELECT id, kind, created_at, capture_started_at, capture_ended_at,
               wall_duration_ticks, speech_duration_ticks, input_device_id,
               status, display_title, generated_title, last_error_code,
               last_error_message, deleted_at, version
        FROM recording
        """;

    private static void AddRecordingParameters(SqliteCommand command, Recording recording)
    {
        command.Parameters.AddWithValue("$id", recording.Id.ToString("D"));
        command.Parameters.AddWithValue("$kind", (int)recording.Kind);
        command.Parameters.AddWithValue("$created_at", SqliteValue.Date(recording.CreatedAt));
        command.Parameters.AddWithValue("$capture_started_at", SqliteValue.Date(recording.CaptureStartedAt));
        command.Parameters.AddWithValue("$capture_ended_at", SqliteValue.NullableDate(recording.CaptureEndedAt));
        command.Parameters.AddWithValue("$wall_duration_ticks", recording.WallDuration.Ticks);
        command.Parameters.AddWithValue("$speech_duration_ticks", recording.SpeechDuration.Ticks);
        command.Parameters.AddWithValue("$input_device_id", (object?)recording.InputDeviceId ?? DBNull.Value);
        command.Parameters.AddWithValue("$status", (int)recording.Status);
        command.Parameters.AddWithValue("$display_title", recording.DisplayTitle);
        command.Parameters.AddWithValue("$generated_title", (object?)recording.GeneratedTitle ?? DBNull.Value);
        command.Parameters.AddWithValue("$last_error_code", (object?)recording.LastErrorCode ?? DBNull.Value);
        command.Parameters.AddWithValue("$last_error_message", (object?)recording.LastErrorMessage ?? DBNull.Value);
        command.Parameters.AddWithValue("$deleted_at", SqliteValue.NullableDate(recording.DeletedAt));
        command.Parameters.AddWithValue("$version", recording.Version);
    }

    private static Recording ReadRecording(SqliteDataReader reader)
    {
        return new Recording(
            reader.GetGuid(0),
            (RecordingKind)reader.GetInt32(1),
            SqliteValue.ReadDate(reader, 2),
            SqliteValue.ReadDate(reader, 3),
            SqliteValue.ReadNullableDate(reader, 4),
            TimeSpan.FromTicks(reader.GetInt64(5)),
            TimeSpan.FromTicks(reader.GetInt64(6)),
            SqliteValue.ReadNullableString(reader, 7),
            (RecordingStatus)reader.GetInt32(8),
            reader.GetString(9),
            SqliteValue.ReadNullableString(reader, 10),
            SqliteValue.ReadNullableString(reader, 11),
            SqliteValue.ReadNullableString(reader, 12),
            SqliteValue.ReadNullableDate(reader, 13),
            reader.GetInt64(14));
    }
}
