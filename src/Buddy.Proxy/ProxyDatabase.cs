using Microsoft.Data.Sqlite;

namespace Buddy.Proxy;

public sealed class ProxyDatabase
{
    public const int SchemaVersion = 1;

    private readonly string _connectionString;

    public ProxyDatabase(ProxyOptions options, IWebHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(environment);
        SqliteRuntime.Initialize();
        string path = options.ResolveDatabasePath(environment.ContentRootPath);
        string? directory = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException("The proxy database directory is invalid.");
        }

        Directory.CreateDirectory(directory);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = false,
        }.ToString();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken)
            .ConfigureAwait(false);
        await using SqliteTransaction transaction =
            (SqliteTransaction)await connection
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);
        await using SqliteCommand countCommand = connection.CreateCommand();
        countCommand.Transaction = transaction;
        countCommand.CommandText =
            "SELECT count(*) FROM sqlite_master WHERE type = 'table' AND name = 'schema_info';";
        long schemaTableCount = (long)(await countCommand
            .ExecuteScalarAsync(cancellationToken)
            .ConfigureAwait(false) ?? 0L);

        if (schemaTableCount == 0)
        {
            await CreateSchemaAsync(connection, transaction, cancellationToken)
                .ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        await using SqliteCommand versionCommand = connection.CreateCommand();
        versionCommand.Transaction = transaction;
        versionCommand.CommandText = "SELECT version FROM schema_info LIMIT 1;";
        long version = (long)(await versionCommand
            .ExecuteScalarAsync(cancellationToken)
            .ConfigureAwait(false) ?? -1L);
        if (version != SchemaVersion)
        {
            throw new InvalidOperationException(
                $"Proxy database schema {version} is incompatible with {SchemaVersion}. "
                + "Stop the proxy and reset its target-local data directory; migrations are intentionally unsupported.");
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<ProxyClient> CreateClientAsync(
        string name,
        string keyPrefix,
        byte[] keyHash,
        int replyLimit,
        long tokenLimit,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(keyPrefix);
        ArgumentNullException.ThrowIfNull(keyHash);
        if (replyLimit <= 0 || tokenLimit <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(replyLimit),
                "Client quotas must be positive.");
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        await using SqliteConnection connection = await OpenAsync(cancellationToken)
            .ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO client_keys (
                name, key_prefix, key_hash, state, reply_limit, token_limit,
                replies_used, prompt_tokens_used, completion_tokens_used,
                created_utc, last_used_utc)
            VALUES (
                $name, $prefix, $hash, 0, $replyLimit, $tokenLimit,
                0, 0, 0, $createdUtc, NULL);
            """;
        command.Parameters.AddWithValue("$name", name.Trim());
        command.Parameters.AddWithValue("$prefix", keyPrefix);
        command.Parameters.Add("$hash", SqliteType.Blob).Value = keyHash;
        command.Parameters.AddWithValue("$replyLimit", replyLimit);
        command.Parameters.AddWithValue("$tokenLimit", tokenLimit);
        command.Parameters.AddWithValue("$createdUtc", now.ToString("O"));
        int inserted = await command
            .ExecuteNonQueryAsync(cancellationToken)
            .ConfigureAwait(false);
        if (inserted != 1)
        {
            throw new InvalidOperationException("The client key was not created.");
        }

        command.CommandText = "SELECT last_insert_rowid();";
        long id = (long)(await command
            .ExecuteScalarAsync(cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("The client key was not created."));
        return new ProxyClient(
            id,
            name.Trim(),
            keyPrefix,
            ProxyKeyState.Active,
            replyLimit,
            tokenLimit,
            0,
            0,
            0,
            now,
            null);
    }

    public async Task<ProxyClient?> FindClientAsync(
        byte[] keyHash,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(keyHash);
        await using SqliteConnection connection = await OpenAsync(cancellationToken)
            .ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = SelectClientSql + " WHERE key_hash = $hash LIMIT 1;";
        command.Parameters.Add("$hash", SqliteType.Blob).Value = keyHash;
        await using SqliteDataReader reader = await command
            .ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? ReadClient(reader)
            : null;
    }

    public async Task<ProxyClient?> FindClientByIdAsync(
        long id,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken)
            .ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = SelectClientSql + " WHERE id = $id LIMIT 1;";
        command.Parameters.AddWithValue("$id", id);
        await using SqliteDataReader reader = await command
            .ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? ReadClient(reader)
            : null;
    }

    public async Task<IReadOnlyList<ProxyClient>> ListClientsAsync(
        CancellationToken cancellationToken = default)
    {
        List<ProxyClient> clients = [];
        await using SqliteConnection connection = await OpenAsync(cancellationToken)
            .ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = SelectClientSql + " ORDER BY id;";
        await using SqliteDataReader reader = await command
            .ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            clients.Add(ReadClient(reader));
        }

        return clients;
    }

    public async Task<ProxyClient> RecordUsageAsync(
        ProxyClient client,
        ProxyUsage usage,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(usage);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        int replyIncrement = usage.CountsAsReply ? 1 : 0;
        await using SqliteConnection connection = await OpenAsync(cancellationToken)
            .ConfigureAwait(false);
        await using SqliteTransaction transaction =
            (SqliteTransaction)await connection
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

        await using (SqliteCommand update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE client_keys
                SET replies_used = replies_used + $replies,
                    prompt_tokens_used = prompt_tokens_used + $prompt,
                    completion_tokens_used = completion_tokens_used + $completion,
                    last_used_utc = $usedUtc
                WHERE id = $id;
                """;
            update.Parameters.AddWithValue("$replies", replyIncrement);
            update.Parameters.AddWithValue("$prompt", usage.PromptTokens);
            update.Parameters.AddWithValue("$completion", usage.CompletionTokens);
            update.Parameters.AddWithValue("$usedUtc", now.ToString("O"));
            update.Parameters.AddWithValue("$id", client.Id);
            int updated = await update
                .ExecuteNonQueryAsync(cancellationToken)
                .ConfigureAwait(false);
            if (updated != 1)
            {
                throw new InvalidOperationException("The client quota record disappeared.");
            }
        }

        await using (SqliteCommand insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO usage_events (
                    request_id, client_id, model, prompt_tokens,
                    completion_tokens, counted_reply, created_utc)
                VALUES (
                    $requestId, $clientId, $model, $prompt,
                    $completion, $reply, $createdUtc);
                """;
            insert.Parameters.AddWithValue("$requestId", usage.RequestId);
            insert.Parameters.AddWithValue("$clientId", client.Id);
            insert.Parameters.AddWithValue("$model", usage.Model);
            insert.Parameters.AddWithValue("$prompt", usage.PromptTokens);
            insert.Parameters.AddWithValue("$completion", usage.CompletionTokens);
            insert.Parameters.AddWithValue("$reply", replyIncrement);
            insert.Parameters.AddWithValue("$createdUtc", now.ToString("O"));
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return (await FindClientByIdAsync(client.Id, cancellationToken).ConfigureAwait(false))
            ?? throw new InvalidOperationException("The updated client could not be read.");
    }

    public async Task<bool> SetClientStateAsync(
        long id,
        ProxyKeyState state,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken)
            .ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "UPDATE client_keys SET state = $state WHERE id = $id;";
        command.Parameters.AddWithValue("$state", (int)state);
        command.Parameters.AddWithValue("$id", id);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        SqliteConnection connection = new(_connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using SqliteCommand pragmas = connection.CreateCommand();
            pragmas.CommandText = "PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 10000;";
            await pragmas.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static async Task CreateSchemaAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $$"""
            CREATE TABLE schema_info (
                version INTEGER NOT NULL
            );
            INSERT INTO schema_info (version) VALUES ({{SchemaVersion}});

            CREATE TABLE client_keys (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL,
                key_prefix TEXT NOT NULL,
                key_hash BLOB NOT NULL UNIQUE,
                state INTEGER NOT NULL CHECK (state IN (0, 1)),
                reply_limit INTEGER NOT NULL CHECK (reply_limit > 0),
                token_limit INTEGER NOT NULL CHECK (token_limit > 0),
                replies_used INTEGER NOT NULL DEFAULT 0 CHECK (replies_used >= 0),
                prompt_tokens_used INTEGER NOT NULL DEFAULT 0 CHECK (prompt_tokens_used >= 0),
                completion_tokens_used INTEGER NOT NULL DEFAULT 0 CHECK (completion_tokens_used >= 0),
                created_utc TEXT NOT NULL,
                last_used_utc TEXT NULL
            );

            CREATE TABLE usage_events (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                request_id TEXT NOT NULL UNIQUE,
                client_id INTEGER NOT NULL,
                model TEXT NOT NULL,
                prompt_tokens INTEGER NOT NULL CHECK (prompt_tokens >= 0),
                completion_tokens INTEGER NOT NULL CHECK (completion_tokens >= 0),
                counted_reply INTEGER NOT NULL CHECK (counted_reply IN (0, 1)),
                created_utc TEXT NOT NULL,
                FOREIGN KEY (client_id) REFERENCES client_keys(id)
            );
            CREATE INDEX usage_events_client_created
                ON usage_events(client_id, created_utc);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static ProxyClient ReadClient(SqliteDataReader reader)
    {
        return new ProxyClient(
            reader.GetInt64(0),
            reader.GetString(1),
            reader.GetString(2),
            (ProxyKeyState)reader.GetInt32(3),
            reader.GetInt32(4),
            reader.GetInt64(5),
            reader.GetInt32(6),
            reader.GetInt64(7),
            reader.GetInt64(8),
            DateTimeOffset.Parse(
                reader.GetString(9),
                System.Globalization.CultureInfo.InvariantCulture),
            reader.IsDBNull(10)
                ? null
                : DateTimeOffset.Parse(
                    reader.GetString(10),
                    System.Globalization.CultureInfo.InvariantCulture));
    }

    private const string SelectClientSql = """
        SELECT id, name, key_prefix, state, reply_limit, token_limit,
               replies_used, prompt_tokens_used, completion_tokens_used,
               created_utc, last_used_utc
        FROM client_keys
        """;
}
