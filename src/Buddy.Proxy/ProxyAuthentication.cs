using System.Net.Http.Headers;

namespace Buddy.Proxy;

public enum AuthenticationFailure
{
    None = 0,
    Invalid = 1,
    Disabled = 2,
}

public sealed record AuthenticationResult(
    ProxyClient? Client,
    AuthenticationFailure Failure);

public sealed class ProxyAuthentication(
    ProxyKeyHasher hasher,
    ProxyDatabase database)
{
    private readonly ProxyKeyHasher _hasher = hasher
        ?? throw new ArgumentNullException(nameof(hasher));
    private readonly ProxyDatabase _database = database
        ?? throw new ArgumentNullException(nameof(database));

    public async Task<AuthenticationResult> AuthenticateAsync(
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        string header = request.Headers.Authorization.ToString();
        if (!AuthenticationHeaderValue.TryParse(
                header,
                out AuthenticationHeaderValue? authorization)
            || !string.Equals(
                authorization.Scheme,
                "Bearer",
                StringComparison.OrdinalIgnoreCase)
            || !ProxyKeyHasher.IsWellFormed(authorization.Parameter))
        {
            return new AuthenticationResult(null, AuthenticationFailure.Invalid);
        }

        ProxyClient? client = await _database
            .FindClientAsync(
                _hasher.Hash(authorization.Parameter!),
                cancellationToken)
            .ConfigureAwait(false);
        if (client is null)
        {
            return new AuthenticationResult(null, AuthenticationFailure.Invalid);
        }

        return client.State == ProxyKeyState.Active
            ? new AuthenticationResult(client, AuthenticationFailure.None)
            : new AuthenticationResult(client, AuthenticationFailure.Disabled);
    }
}
