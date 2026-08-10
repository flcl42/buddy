using Buddy.Core.Abstractions;
using Buddy.Core.Domain;
using Microsoft.Data.Sqlite;

namespace Buddy.Persistence;

public sealed class SqliteDialogRepository : IDialogRepository
{
    private readonly SqliteConnectionFactory _connections;

    public SqliteDialogRepository(SqliteConnectionFactory connections)
    {
        _connections = connections ?? throw new ArgumentNullException(nameof(connections));
    }

    public async Task AddSessionAsync(
        DialogSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        await using SqliteConnection connection =
            await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO dialog_session (
                id, recording_id, status, started_at, ended_at,
                system_instruction, provider, model, last_error, version)
            VALUES (
                $id, $recording_id, $status, $started_at, $ended_at,
                $system_instruction, $provider, $model, $last_error, $version);
            """;
        AddSessionParameters(command, session);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task<DialogSession?> GetSessionAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        if (sessionId == Guid.Empty)
        {
            throw new ArgumentException(
                "A dialog session identifier is required.",
                nameof(sessionId));
        }

        return GetSingleSessionAsync(
            "WHERE id = $value",
            sessionId.ToString("D"),
            cancellationToken);
    }

    public Task<DialogSession?> GetSessionByRecordingIdAsync(
        Guid recordingId,
        CancellationToken cancellationToken = default)
    {
        if (recordingId == Guid.Empty)
        {
            throw new ArgumentException(
                "A recording identifier is required.",
                nameof(recordingId));
        }

        return GetSingleSessionAsync(
            "WHERE recording_id = $value",
            recordingId.ToString("D"),
            cancellationToken);
    }

    public Task<DialogSession?> GetLatestSessionAsync(
        CancellationToken cancellationToken = default)
    {
        return GetSingleSessionAsync(
            "ORDER BY started_at DESC LIMIT 1",
            null,
            cancellationToken);
    }

    public Task<DialogSession?> GetActiveSessionAsync(
        CancellationToken cancellationToken = default)
    {
        return GetSingleSessionAsync(
            "WHERE status IN (0, 1) ORDER BY started_at DESC LIMIT 1",
            null,
            cancellationToken);
    }

    public async Task<bool> TryUpdateSessionAsync(
        DialogSession session,
        long expectedVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentOutOfRangeException.ThrowIfNegative(expectedVersion);

        await using SqliteConnection connection =
            await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            UPDATE dialog_session
            SET recording_id = $recording_id,
                status = $status,
                started_at = $started_at,
                ended_at = $ended_at,
                system_instruction = $system_instruction,
                provider = $provider,
                model = $model,
                last_error = $last_error,
                version = $version
            WHERE id = $id AND version = $expected_version;
            """;
        AddSessionParameters(command, session);
        command.Parameters.AddWithValue("$expected_version", expectedVersion);
        int affected = await command.ExecuteNonQueryAsync(cancellationToken)
            .ConfigureAwait(false);
        return affected == 1;
    }

    public async Task AddMessageAsync(
        DialogMessage message,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection =
            await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await InsertMessageAsync(
                connection,
                transaction: null,
                message,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task AddUserMessageWithPronunciationAsync(
        DialogMessage message,
        DialogPronunciationAssessment assessment,
        CancellationToken cancellationToken = default)
    {
        ValidateAssessment(message, assessment);
        await using SqliteConnection connection =
            await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction =
            (SqliteTransaction)await connection
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);
        await InsertMessageAsync(connection, transaction, message, cancellationToken)
            .ConfigureAwait(false);
        await InsertAssessmentAsync(
                connection,
                transaction,
                assessment,
                cancellationToken)
            .ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateMessageAudioAsync(
        Guid messageId,
        Guid audioArtifactId,
        CancellationToken cancellationToken = default)
    {
        if (messageId == Guid.Empty)
        {
            throw new ArgumentException(
                "A dialog message identifier is required.",
                nameof(messageId));
        }

        if (audioArtifactId == Guid.Empty)
        {
            throw new ArgumentException(
                "An audio artifact identifier is required.",
                nameof(audioArtifactId));
        }

        await using SqliteConnection connection =
            await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            UPDATE dialog_message
            SET audio_artifact_id = $audio_artifact_id
            WHERE id = $id AND role = $assistant_role;
            """;
        command.Parameters.AddWithValue("$id", messageId.ToString("D"));
        command.Parameters.AddWithValue(
            "$audio_artifact_id",
            audioArtifactId.ToString("D"));
        command.Parameters.AddWithValue(
            "$assistant_role",
            (int)DialogMessageRole.Assistant);
        int affected = await command.ExecuteNonQueryAsync(cancellationToken)
            .ConfigureAwait(false);
        if (affected != 1)
        {
            throw new InvalidOperationException(
                "The assistant message could not be linked to generated audio.");
        }
    }

    public async Task<IReadOnlyList<DialogMessage>> GetMessagesAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        if (sessionId == Guid.Empty)
        {
            throw new ArgumentException(
                "A dialog session identifier is required.",
                nameof(sessionId));
        }

        await using SqliteConnection connection =
            await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, session_id, sequence, role, text, created_at,
                   provider, model, latency_ticks, prompt_tokens,
                   completion_tokens, audio_artifact_id
            FROM dialog_message
            WHERE session_id = $session_id
            ORDER BY sequence;
            """;
        command.Parameters.AddWithValue("$session_id", sessionId.ToString("D"));

        List<DialogMessage> messages = [];
        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            messages.Add(ReadMessage(reader));
        }

        return messages;
    }

    public async Task ReplacePronunciationAssessmentAsync(
        DialogPronunciationAssessment assessment,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assessment);
        ValidateAssessmentWords(assessment);
        await using SqliteConnection connection =
            await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction =
            (SqliteTransaction)await connection
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);
        await using (SqliteCommand delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = """
                DELETE FROM dialog_pronunciation_assessment
                WHERE message_id = $message_id;
                """;
            delete.Parameters.AddWithValue(
                "$message_id",
                assessment.MessageId.ToString("D"));
            await delete.ExecuteNonQueryAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        await InsertAssessmentAsync(
                connection,
                transaction,
                assessment,
                cancellationToken)
            .ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyDictionary<Guid, DialogPronunciationAssessment>>
        GetPronunciationAssessmentsAsync(
            Guid sessionId,
            CancellationToken cancellationToken = default)
    {
        if (sessionId == Guid.Empty)
        {
            throw new ArgumentException(
                "A dialog session identifier is required.",
                nameof(sessionId));
        }

        await using SqliteConnection connection =
            await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        Dictionary<Guid, AssessmentMetadata> metadata = [];
        await using (SqliteCommand assessments = connection.CreateCommand())
        {
            assessments.CommandText = """
                SELECT dpa.message_id, dpa.transcript,
                       dpa.phonetic_transcript, dpa.created_at,
                       dpa.model, dpa.schema_version
                FROM dialog_pronunciation_assessment dpa
                INNER JOIN dialog_message dm ON dm.id = dpa.message_id
                WHERE dm.session_id = $session_id
                ORDER BY dm.sequence;
                """;
            assessments.Parameters.AddWithValue(
                "$session_id",
                sessionId.ToString("D"));
            await using SqliteDataReader reader =
                await assessments.ExecuteReaderAsync(cancellationToken)
                    .ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                Guid messageId = reader.GetGuid(0);
                metadata.Add(
                    messageId,
                    new AssessmentMetadata(
                        reader.GetString(1),
                        reader.GetString(2),
                        SqliteValue.ReadDate(reader, 3),
                        reader.GetString(4),
                        reader.GetString(5),
                        []));
            }
        }

        if (metadata.Count > 0)
        {
            await using SqliteCommand words = connection.CreateCommand();
            words.CommandText = """
                SELECT dpw.message_id, dpw.sequence, dpw.text,
                       dpw.start_ticks, dpw.end_ticks, dpw.confidence
                FROM dialog_pronunciation_word dpw
                INNER JOIN dialog_message dm ON dm.id = dpw.message_id
                WHERE dm.session_id = $session_id
                ORDER BY dm.sequence, dpw.sequence;
                """;
            words.Parameters.AddWithValue("$session_id", sessionId.ToString("D"));
            await using SqliteDataReader reader =
                await words.ExecuteReaderAsync(cancellationToken)
                    .ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                Guid messageId = reader.GetGuid(0);
                if (metadata.TryGetValue(messageId, out AssessmentMetadata? item))
                {
                    item.Words.Add(
                        new PronunciationWord(
                            messageId,
                            reader.GetInt32(1),
                            reader.GetString(2),
                            TimeSpan.FromTicks(reader.GetInt64(3)),
                            TimeSpan.FromTicks(reader.GetInt64(4)),
                            reader.GetFloat(5)));
                }
            }
        }

        return metadata.ToDictionary(
            pair => pair.Key,
            pair => new DialogPronunciationAssessment(
                pair.Key,
                pair.Value.Transcript,
                pair.Value.PhoneticTranscript,
                pair.Value.CreatedAt,
                pair.Value.Model,
                pair.Value.SchemaVersion,
                pair.Value.Words));
    }

    private async Task<DialogSession?> GetSingleSessionAsync(
        string clause,
        string? value,
        CancellationToken cancellationToken)
    {
        await using SqliteConnection connection =
            await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT id, recording_id, status, started_at, ended_at,
                   system_instruction, provider, model, last_error, version
            FROM dialog_session
            {clause};
            """;
        if (value is not null)
        {
            command.Parameters.AddWithValue("$value", value);
        }

        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? ReadSession(reader)
            : null;
    }

    private static void AddSessionParameters(
        SqliteCommand command,
        DialogSession session)
    {
        command.Parameters.AddWithValue("$id", session.Id.ToString("D"));
        command.Parameters.AddWithValue(
            "$recording_id",
            session.RecordingId.ToString("D"));
        command.Parameters.AddWithValue("$status", (int)session.Status);
        command.Parameters.AddWithValue(
            "$started_at",
            SqliteValue.Date(session.StartedAt));
        command.Parameters.AddWithValue(
            "$ended_at",
            SqliteValue.NullableDate(session.EndedAt));
        command.Parameters.AddWithValue(
            "$system_instruction",
            session.SystemInstruction);
        command.Parameters.AddWithValue(
            "$provider",
            (object?)session.Provider ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$model",
            (object?)session.Model ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$last_error",
            (object?)session.LastError ?? DBNull.Value);
        command.Parameters.AddWithValue("$version", session.Version);
    }

    private static DialogSession ReadSession(SqliteDataReader reader)
    {
        return new DialogSession(
            reader.GetGuid(0),
            reader.GetGuid(1),
            (DialogSessionStatus)reader.GetInt32(2),
            SqliteValue.ReadDate(reader, 3),
            SqliteValue.ReadNullableDate(reader, 4),
            reader.GetString(5),
            SqliteValue.ReadNullableString(reader, 6),
            SqliteValue.ReadNullableString(reader, 7),
            SqliteValue.ReadNullableString(reader, 8),
            reader.GetInt64(9));
    }

    private static DialogMessage ReadMessage(SqliteDataReader reader)
    {
        return new DialogMessage(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetInt32(2),
            (DialogMessageRole)reader.GetInt32(3),
            reader.GetString(4),
            SqliteValue.ReadDate(reader, 5),
            SqliteValue.ReadNullableString(reader, 6),
            SqliteValue.ReadNullableString(reader, 7),
            reader.IsDBNull(8) ? null : TimeSpan.FromTicks(reader.GetInt64(8)),
            reader.IsDBNull(9) ? null : reader.GetInt32(9),
            reader.IsDBNull(10) ? null : reader.GetInt32(10),
            reader.IsDBNull(11) ? null : reader.GetGuid(11));
    }

    private static async Task InsertMessageAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        DialogMessage message,
        CancellationToken cancellationToken)
    {
        ValidateMessage(message);
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO dialog_message (
                id, session_id, sequence, role, text, created_at,
                provider, model, latency_ticks, prompt_tokens,
                completion_tokens, audio_artifact_id)
            VALUES (
                $id, $session_id, $sequence, $role, $text, $created_at,
                $provider, $model, $latency_ticks, $prompt_tokens,
                $completion_tokens, $audio_artifact_id);
            """;
        command.Parameters.AddWithValue("$id", message.Id.ToString("D"));
        command.Parameters.AddWithValue(
            "$session_id",
            message.SessionId.ToString("D"));
        command.Parameters.AddWithValue("$sequence", message.Sequence);
        command.Parameters.AddWithValue("$role", (int)message.Role);
        command.Parameters.AddWithValue("$text", message.Text.Trim());
        command.Parameters.AddWithValue(
            "$created_at",
            SqliteValue.Date(message.CreatedAt));
        command.Parameters.AddWithValue(
            "$provider",
            (object?)message.Provider ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$model",
            (object?)message.Model ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$latency_ticks",
            message.Latency.HasValue ? message.Latency.Value.Ticks : DBNull.Value);
        command.Parameters.AddWithValue(
            "$prompt_tokens",
            message.PromptTokens.HasValue
                ? message.PromptTokens.Value
                : DBNull.Value);
        command.Parameters.AddWithValue(
            "$completion_tokens",
            message.CompletionTokens.HasValue
                ? message.CompletionTokens.Value
                : DBNull.Value);
        command.Parameters.AddWithValue(
            "$audio_artifact_id",
            message.AudioArtifactId.HasValue
                ? message.AudioArtifactId.Value.ToString("D")
                : DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task InsertAssessmentAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        DialogPronunciationAssessment assessment,
        CancellationToken cancellationToken)
    {
        ValidateAssessmentWords(assessment);
        await using (SqliteCommand insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO dialog_pronunciation_assessment (
                    message_id, transcript, phonetic_transcript, created_at,
                    model, schema_version)
                VALUES (
                    $message_id, $transcript, $phonetic_transcript, $created_at,
                    $model, $schema_version);
                """;
            insert.Parameters.AddWithValue(
                "$message_id",
                assessment.MessageId.ToString("D"));
            insert.Parameters.AddWithValue("$transcript", assessment.Transcript);
            insert.Parameters.AddWithValue(
                "$phonetic_transcript",
                assessment.PhoneticTranscript);
            insert.Parameters.AddWithValue(
                "$created_at",
                SqliteValue.Date(assessment.CreatedAt));
            insert.Parameters.AddWithValue("$model", assessment.Model);
            insert.Parameters.AddWithValue(
                "$schema_version",
                assessment.SchemaVersion);
            await insert.ExecuteNonQueryAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        foreach (PronunciationWord word in assessment.Words)
        {
            await using SqliteCommand insertWord = connection.CreateCommand();
            insertWord.Transaction = transaction;
            insertWord.CommandText = """
                INSERT INTO dialog_pronunciation_word (
                    message_id, sequence, text, start_ticks, end_ticks, confidence)
                VALUES (
                    $message_id, $sequence, $text, $start_ticks, $end_ticks,
                    $confidence);
                """;
            insertWord.Parameters.AddWithValue(
                "$message_id",
                assessment.MessageId.ToString("D"));
            insertWord.Parameters.AddWithValue("$sequence", word.Sequence);
            insertWord.Parameters.AddWithValue("$text", word.Text);
            insertWord.Parameters.AddWithValue("$start_ticks", word.Start.Ticks);
            insertWord.Parameters.AddWithValue("$end_ticks", word.End.Ticks);
            insertWord.Parameters.AddWithValue("$confidence", word.Confidence);
            await insertWord.ExecuteNonQueryAsync(cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static void ValidateMessage(DialogMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.Id == Guid.Empty || message.SessionId == Guid.Empty)
        {
            throw new ArgumentException(
                "A dialog message must have an identity and belong to a session.",
                nameof(message));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(message.Sequence);
        ArgumentException.ThrowIfNullOrWhiteSpace(message.Text);
    }

    private static void ValidateAssessment(
        DialogMessage message,
        DialogPronunciationAssessment assessment)
    {
        ValidateMessage(message);
        ArgumentNullException.ThrowIfNull(assessment);
        if (message.Role != DialogMessageRole.User
            || assessment.MessageId != message.Id
            || !string.Equals(
                assessment.Transcript.Trim(),
                message.Text.Trim(),
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The pronunciation assessment must match the user message.",
                nameof(assessment));
        }

        ValidateAssessmentWords(assessment);
    }

    private static void ValidateAssessmentWords(
        DialogPronunciationAssessment assessment)
    {
        ArgumentNullException.ThrowIfNull(assessment);
        if (assessment.MessageId == Guid.Empty)
        {
            throw new ArgumentException(
                "A dialog pronunciation assessment must belong to a message.",
                nameof(assessment));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(assessment.Transcript);
        ArgumentException.ThrowIfNullOrWhiteSpace(assessment.Model);
        ArgumentException.ThrowIfNullOrWhiteSpace(assessment.SchemaVersion);
        for (int index = 0; index < assessment.Words.Count; index++)
        {
            PronunciationWord word = assessment.Words[index];
            if (word.SourceId != assessment.MessageId || word.Sequence != index)
            {
                throw new ArgumentException(
                    "Dialog pronunciation words must belong to the message "
                    + "and have contiguous sequence numbers.",
                    nameof(assessment));
            }
        }
    }

    private sealed record AssessmentMetadata(
        string Transcript,
        string PhoneticTranscript,
        DateTimeOffset CreatedAt,
        string Model,
        string SchemaVersion,
        List<PronunciationWord> Words);
}
