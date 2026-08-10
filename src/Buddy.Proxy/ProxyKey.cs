using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Buddy.Proxy;

public sealed partial class ProxyKeyHasher
{
    private readonly byte[] _pepper;

    public ProxyKeyHasher(ProxyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _pepper = Encoding.UTF8.GetBytes(options.KeyPepper);
    }

    public static string CreateKey()
    {
        Span<byte> bytes = stackalloc byte[16];
        RandomNumberGenerator.Fill(bytes);
        return "bpk_" + Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    public static bool IsWellFormed(string? value) =>
        value is not null && KeyPattern().IsMatch(value);

    public byte[] Hash(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        using HMACSHA256 hmac = new(_pepper);
        return hmac.ComputeHash(Encoding.UTF8.GetBytes(key));
    }

    public static string GetDisplayPrefix(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return key.Length <= 10 ? key : key[..10];
    }

    [GeneratedRegex("^bpk_[A-Za-z0-9_-]{22}$", RegexOptions.CultureInvariant)]
    private static partial Regex KeyPattern();
}
