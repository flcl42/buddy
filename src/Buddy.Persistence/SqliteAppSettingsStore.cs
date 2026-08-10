using System.Text.Json;
using Buddy.Core.Abstractions;
using Microsoft.Data.Sqlite;

namespace Buddy.Persistence;

public sealed class SqliteAppSettingsStore : IAppSettingsStore
{
    private readonly SqliteConnectionFactory _connections;

    public SqliteAppSettingsStore(SqliteConnectionFactory connections)
    {
        _connections = connections ?? throw new ArgumentNullException(nameof(connections));
    }

    public async Task<string?> GetAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        ValidateKey(key);

        await using SqliteConnection connection =
            await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT value_json
            FROM app_setting
            WHERE key = $key;
            """;
        command.Parameters.AddWithValue("$key", key);

        object? result = await command
            .ExecuteScalarAsync(cancellationToken)
            .ConfigureAwait(false);
        if (result is null or DBNull)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<string>((string)result);
        }
        catch (JsonException error)
        {
            throw new InvalidDataException(
                $"The saved value for setting '{key}' is invalid.",
                error);
        }
    }

    public async Task SetAsync(
        string key,
        string value,
        CancellationToken cancellationToken = default)
    {
        ValidateKey(key);
        ArgumentNullException.ThrowIfNull(value);

        await using SqliteConnection connection =
            await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO app_setting (key, value_json, updated_at)
            VALUES ($key, $value_json, $updated_at)
            ON CONFLICT (key) DO UPDATE SET
                value_json = excluded.value_json,
                updated_at = excluded.updated_at;
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value_json", JsonSerializer.Serialize(value));
        command.Parameters.AddWithValue(
            "$updated_at",
            DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RemoveAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        ValidateKey(key);

        await using SqliteConnection connection =
            await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "DELETE FROM app_setting WHERE key = $key;";
        command.Parameters.AddWithValue("$key", key);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void ValidateKey(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (key.Length > 128)
        {
            throw new ArgumentOutOfRangeException(
                nameof(key),
                "Setting keys may not exceed 128 characters.");
        }
    }
}
