using System.Security.Cryptography;
using System.Text;
using Buddy.Core.Abstractions;
using Buddy.Persistence;

namespace Buddy.App.Services;

public sealed class DpapiSecretStore : ISecretStore
{
    private static readonly byte[] OptionalEntropy = Encoding.UTF8.GetBytes("Buddy.SecretStore.v1");
    private readonly string _secretDirectory;

    public DpapiSecretStore(BuddyDataPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        _secretDirectory = Path.Combine(paths.Root, "secrets");
    }

    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        string path = GetPath(key);
        if (!File.Exists(path))
        {
            return null;
        }

        byte[] protectedBytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        byte[] plaintext = ProtectedData.Unprotect(
            protectedBytes,
            OptionalEntropy,
            DataProtectionScope.CurrentUser);

        try
        {
            return Encoding.UTF8.GetString(plaintext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public async Task SetAsync(
        string key,
        string value,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Directory.CreateDirectory(_secretDirectory);

        string path = GetPath(key);
        string temporaryPath = path + $".{Guid.NewGuid():N}.tmp";
        byte[] plaintext = Encoding.UTF8.GetBytes(value);
        byte[]? protectedBytes = null;

        try
        {
            protectedBytes = ProtectedData.Protect(
                plaintext,
                OptionalEntropy,
                DataProtectionScope.CurrentUser);
            await File.WriteAllBytesAsync(temporaryPath, protectedBytes, cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
            if (protectedBytes is not null)
            {
                CryptographicOperations.ZeroMemory(protectedBytes);
            }

            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string path = GetPath(key);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    private string GetPath(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        string fileName = Convert.ToHexString(hash).ToLowerInvariant() + ".secret";
        return Path.Combine(_secretDirectory, fileName);
    }
}
