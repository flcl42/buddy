using Buddy.Core.Abstractions;
using Buddy.Core.Domain;
using Microsoft.Data.Sqlite;

namespace Buddy.Persistence;

public sealed class SqliteBackgroundJobStore : IBackgroundJobStore
{
    private readonly SqliteConnectionFactory _connections;

    public SqliteBackgroundJobStore(SqliteConnectionFactory connections)
    {
        _connections = connections ?? throw new ArgumentNullException(nameof(connections));
    }

    public async Task EnqueueAsync(BackgroundJob job, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);

        await using SqliteConnection connection =
            await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"""
            INSERT INTO background_job (
                id, recording_id, type, payload_json, state, attempt_count,
                created_at, available_at, lease_expires_at, lease_owner,
                last_error_code, last_error_message)
            VALUES (
                $id, $recording_id, $type, $payload_json, $state, $attempt_count,
                $created_at, $available_at, $lease_expires_at, $lease_owner,
                $last_error_code, $last_error_message);
            """;
        AddParameters(command, job);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> EnqueueIfMissingAsync(
        BackgroundJob job,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);

        await using SqliteConnection connection =
            await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR IGNORE INTO background_job (
                id, recording_id, type, payload_json, state, attempt_count,
                created_at, available_at, lease_expires_at, lease_owner,
                last_error_code, last_error_message)
            VALUES (
                $id, $recording_id, $type, $payload_json, $state, $attempt_count,
                $created_at, $available_at, $lease_expires_at, $lease_owner,
                $last_error_code, $last_error_message);
            """;
        AddParameters(command, job);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
    }

    public async Task<BackgroundJob?> TryLeaseNextAsync(
        string workerId,
        TimeSpan leaseDuration,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(leaseDuration, TimeSpan.Zero);

        await using SqliteConnection connection =
            await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction = connection.BeginTransaction(deferred: false);
        Guid? jobId = null;

        await using (SqliteCommand select = connection.CreateCommand())
        {
            select.Transaction = transaction;
            select.CommandText = """
                SELECT id
                FROM background_job
                WHERE available_at <= $now
                  AND (
                      state = $pending
                      OR (state = $running AND lease_expires_at <= $now)
                  )
                ORDER BY available_at, created_at
                LIMIT 1;
                """;
            select.Parameters.AddWithValue("$now", SqliteValue.Date(now));
            select.Parameters.AddWithValue("$pending", (int)BackgroundJobState.Pending);
            select.Parameters.AddWithValue("$running", (int)BackgroundJobState.Running);
            object? selected = await select.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            if (selected is string text)
            {
                jobId = Guid.Parse(text);
            }
        }

        if (!jobId.HasValue)
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return null;
        }

        await using (SqliteCommand update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE background_job
                SET state = $running,
                    attempt_count = attempt_count + 1,
                    lease_owner = $worker_id,
                    lease_expires_at = $lease_expires_at
                WHERE id = $id;
                """;
            update.Parameters.AddWithValue("$running", (int)BackgroundJobState.Running);
            update.Parameters.AddWithValue("$worker_id", workerId);
            update.Parameters.AddWithValue("$lease_expires_at", SqliteValue.Date(now + leaseDuration));
            update.Parameters.AddWithValue("$id", jobId.Value.ToString("D"));
            await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        BackgroundJob leased;
        await using (SqliteCommand read = connection.CreateCommand())
        {
            read.Transaction = transaction;
            read.CommandText = $"{JobSelect} WHERE id = $id;";
            read.Parameters.AddWithValue("$id", jobId.Value.ToString("D"));
            await using SqliteDataReader reader =
                await read.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidOperationException("Leased background job disappeared before it could be read.");
            }

            leased = ReadJob(reader);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return leased;
    }

    public async Task CompleteAsync(
        Guid jobId,
        string workerId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);

        int affected = await ChangeOwnedJobAsync(
            jobId,
            workerId,
            """
            state = $completed,
            lease_owner = NULL,
            lease_expires_at = NULL,
            last_error_code = NULL,
            last_error_message = NULL
            """,
            command => command.Parameters.AddWithValue("$completed", (int)BackgroundJobState.Completed),
            cancellationToken).ConfigureAwait(false);

        if (affected != 1)
        {
            throw new InvalidOperationException("The background job is not leased by this worker.");
        }
    }

    public async Task<bool> RenewLeaseAsync(
        Guid jobId,
        string workerId,
        DateTimeOffset leaseExpiresAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);

        int affected = await ChangeOwnedJobAsync(
            jobId,
            workerId,
            "lease_expires_at = $lease_expires_at",
            command => command.Parameters.AddWithValue(
                "$lease_expires_at",
                SqliteValue.Date(leaseExpiresAt)),
            cancellationToken).ConfigureAwait(false);
        return affected == 1;
    }

    public async Task ReleaseAsync(
        Guid jobId,
        string workerId,
        DateTimeOffset availableAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);

        int affected = await ChangeOwnedJobAsync(
            jobId,
            workerId,
            """
            state = $pending,
            available_at = $available_at,
            lease_owner = NULL,
            lease_expires_at = NULL
            """,
            command =>
            {
                command.Parameters.AddWithValue("$pending", (int)BackgroundJobState.Pending);
                command.Parameters.AddWithValue("$available_at", SqliteValue.Date(availableAt));
            },
            cancellationToken).ConfigureAwait(false);

        if (affected != 1)
        {
            throw new InvalidOperationException("The background job is not leased by this worker.");
        }
    }

    public async Task FailAsync(
        Guid jobId,
        string workerId,
        string errorCode,
        string errorMessage,
        DateTimeOffset retryAt,
        int maximumAttempts,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumAttempts, 1);

        int affected = await ChangeOwnedJobAsync(
            jobId,
            workerId,
            """
            state = CASE
                        WHEN attempt_count >= $maximum_attempts THEN $failed
                        ELSE $pending
                    END,
            available_at = $available_at,
            lease_owner = NULL,
            lease_expires_at = NULL,
            last_error_code = $error_code,
            last_error_message = $error_message
            """,
            command =>
            {
                command.Parameters.AddWithValue("$maximum_attempts", maximumAttempts);
                command.Parameters.AddWithValue("$failed", (int)BackgroundJobState.Failed);
                command.Parameters.AddWithValue("$pending", (int)BackgroundJobState.Pending);
                command.Parameters.AddWithValue("$available_at", SqliteValue.Date(retryAt));
                command.Parameters.AddWithValue("$error_code", errorCode);
                command.Parameters.AddWithValue("$error_message", errorMessage);
            },
            cancellationToken).ConfigureAwait(false);

        if (affected != 1)
        {
            throw new InvalidOperationException("The background job is not leased by this worker.");
        }
    }

    private async Task<int> ChangeOwnedJobAsync(
        Guid jobId,
        string workerId,
        string setClause,
        Action<SqliteCommand> addParameters,
        CancellationToken cancellationToken)
    {
        await using SqliteConnection connection =
            await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"""
            UPDATE background_job
            SET {setClause}
            WHERE id = $id
              AND state = $running
              AND lease_owner = $worker_id;
            """;
        command.Parameters.AddWithValue("$id", jobId.ToString("D"));
        command.Parameters.AddWithValue("$running", (int)BackgroundJobState.Running);
        command.Parameters.AddWithValue("$worker_id", workerId);
        addParameters(command);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private const string JobSelect = """
        SELECT id, recording_id, type, payload_json, state, attempt_count,
               created_at, available_at, lease_expires_at, lease_owner,
               last_error_code, last_error_message
        FROM background_job
        """;

    private static void AddParameters(SqliteCommand command, BackgroundJob job)
    {
        command.Parameters.AddWithValue("$id", job.Id.ToString("D"));
        command.Parameters.AddWithValue(
            "$recording_id",
            job.RecordingId.HasValue ? job.RecordingId.Value.ToString("D") : DBNull.Value);
        command.Parameters.AddWithValue("$type", (int)job.Type);
        command.Parameters.AddWithValue("$payload_json", job.PayloadJson);
        command.Parameters.AddWithValue("$state", (int)job.State);
        command.Parameters.AddWithValue("$attempt_count", job.AttemptCount);
        command.Parameters.AddWithValue("$created_at", SqliteValue.Date(job.CreatedAt));
        command.Parameters.AddWithValue("$available_at", SqliteValue.Date(job.AvailableAt));
        command.Parameters.AddWithValue("$lease_expires_at", SqliteValue.NullableDate(job.LeaseExpiresAt));
        command.Parameters.AddWithValue("$lease_owner", (object?)job.LeaseOwner ?? DBNull.Value);
        command.Parameters.AddWithValue("$last_error_code", (object?)job.LastErrorCode ?? DBNull.Value);
        command.Parameters.AddWithValue("$last_error_message", (object?)job.LastErrorMessage ?? DBNull.Value);
    }

    private static BackgroundJob ReadJob(SqliteDataReader reader)
    {
        return new BackgroundJob(
            reader.GetGuid(0),
            reader.IsDBNull(1) ? null : reader.GetGuid(1),
            (BackgroundJobType)reader.GetInt32(2),
            reader.GetString(3),
            (BackgroundJobState)reader.GetInt32(4),
            reader.GetInt32(5),
            SqliteValue.ReadDate(reader, 6),
            SqliteValue.ReadDate(reader, 7),
            SqliteValue.ReadNullableDate(reader, 8),
            SqliteValue.ReadNullableString(reader, 9),
            SqliteValue.ReadNullableString(reader, 10),
            SqliteValue.ReadNullableString(reader, 11));
    }
}
