using System.Security.Cryptography;
using System.Text;
using Buddy.Core.Abstractions;

namespace Buddy.App.Services;

/// <summary>
/// Uses the host credential store through MAUI SecureStorage. Keys are hashed
/// so provider names never become native preference identifiers.
/// </summary>
public sealed class MauiSecureSecretStore : ISecretStore
{
    private const string KeyPrefix = "buddy-v1-";

    public Task<string?> GetAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return SecureStorage.Default.GetAsync(GetStorageKey(key));
    }

    public async Task SetAsync(
        string key,
        string value,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        cancellationToken.ThrowIfCancellationRequested();
        await SecureStorage.Default
            .SetAsync(GetStorageKey(key), value)
            .ConfigureAwait(false);
    }

    public Task RemoveAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = SecureStorage.Default.Remove(GetStorageKey(key));
        return Task.CompletedTask;
    }

    private static string GetStorageKey(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return KeyPrefix + Convert.ToHexString(hash).ToLowerInvariant();
    }
}
