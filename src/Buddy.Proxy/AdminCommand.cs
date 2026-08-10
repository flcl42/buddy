using System.Text.Json;

namespace Buddy.Proxy;

public static class AdminCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public static async Task<int> RunAsync(
        IServiceProvider services,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(arguments);
        ProxyDatabase database = services.GetRequiredService<ProxyDatabase>();
        ProxyOptions options = services.GetRequiredService<ProxyOptions>();
        ProxyKeyHasher hasher = services.GetRequiredService<ProxyKeyHasher>();
        if (arguments.Count == 0)
        {
            WriteUsage();
            return 2;
        }

        switch (arguments[0])
        {
            case "create":
                return await CreateAsync(
                        database,
                        options,
                        hasher,
                        arguments.Skip(1).ToArray(),
                        cancellationToken)
                    .ConfigureAwait(false);
            case "list":
                return await ListAsync(database, cancellationToken).ConfigureAwait(false);
            case "disable":
                return await SetStateAsync(
                        database,
                        arguments.Skip(1).ToArray(),
                        ProxyKeyState.Disabled,
                        cancellationToken)
                    .ConfigureAwait(false);
            case "enable":
                return await SetStateAsync(
                        database,
                        arguments.Skip(1).ToArray(),
                        ProxyKeyState.Active,
                        cancellationToken)
                    .ConfigureAwait(false);
            default:
                WriteUsage();
                return 2;
        }
    }

    private static async Task<int> CreateAsync(
        ProxyDatabase database,
        ProxyOptions options,
        ProxyKeyHasher hasher,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        string name = GetOption(arguments, "--name") ?? "buddy-client";
        int replyLimit = ParsePositiveInt(
            GetOption(arguments, "--reply-limit"),
            options.DefaultReplyLimit,
            "--reply-limit");
        long tokenLimit = ParsePositiveLong(
            GetOption(arguments, "--token-limit"),
            options.DefaultTokenLimit,
            "--token-limit");
        string key = ProxyKeyHasher.CreateKey();
        ProxyClient client = await database
            .CreateClientAsync(
                name,
                ProxyKeyHasher.GetDisplayPrefix(key),
                hasher.Hash(key),
                replyLimit,
                tokenLimit,
                cancellationToken)
            .ConfigureAwait(false);
        Console.WriteLine(
            JsonSerializer.Serialize(
                new
                {
                    client.Id,
                    client.Name,
                    key,
                    client.ReplyLimit,
                    client.TokenLimit,
                    warning = "The plaintext key is shown once. Store it in the Buddy release secret.",
                },
                JsonOptions));
        return 0;
    }

    private static async Task<int> ListAsync(
        ProxyDatabase database,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ProxyClient> clients = await database
            .ListClientsAsync(cancellationToken)
            .ConfigureAwait(false);
        Console.WriteLine(
            JsonSerializer.Serialize(
                clients.Select(
                    client => new
                    {
                        client.Id,
                        client.Name,
                        client.KeyPrefix,
                        state = client.State.ToString().ToLowerInvariant(),
                        quota = ProxyQuotaResponse.FromClient(client),
                        client.CreatedUtc,
                        client.LastUsedUtc,
                    }),
                JsonOptions));
        return 0;
    }

    private static async Task<int> SetStateAsync(
        ProxyDatabase database,
        IReadOnlyList<string> arguments,
        ProxyKeyState state,
        CancellationToken cancellationToken)
    {
        string? rawId = GetOption(arguments, "--id");
        if (!long.TryParse(
                rawId,
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out long id)
            || id <= 0)
        {
            throw new ArgumentException("--id must be a positive client id.");
        }

        bool changed = await database
            .SetClientStateAsync(id, state, cancellationToken)
            .ConfigureAwait(false);
        if (!changed)
        {
            Console.Error.WriteLine($"Client {id} was not found.");
            return 1;
        }

        Console.WriteLine($"Client {id} is now {state.ToString().ToLowerInvariant()}.");
        return 0;
    }

    private static string? GetOption(IReadOnlyList<string> arguments, string name)
    {
        for (int index = 0; index < arguments.Count; index++)
        {
            if (string.Equals(arguments[index], name, StringComparison.Ordinal))
            {
                return index + 1 < arguments.Count ? arguments[index + 1] : null;
            }
        }

        return null;
    }

    private static int ParsePositiveInt(string? raw, int fallback, string name)
    {
        if (raw is null)
        {
            return fallback;
        }

        if (!int.TryParse(
                raw,
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out int value)
            || value <= 0)
        {
            throw new ArgumentException($"{name} must be a positive integer.");
        }

        return value;
    }

    private static long ParsePositiveLong(string? raw, long fallback, string name)
    {
        if (raw is null)
        {
            return fallback;
        }

        if (!long.TryParse(
                raw,
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out long value)
            || value <= 0)
        {
            throw new ArgumentException($"{name} must be a positive integer.");
        }

        return value;
    }

    private static void WriteUsage()
    {
        Console.Error.WriteLine(
            "Usage: buddy-proxy admin create [--name NAME] [--reply-limit 1000] "
            + "[--token-limit 1000000] | list | disable --id ID | enable --id ID");
    }
}
