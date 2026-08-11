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
        Span<char> key = stackalloc char[13];
        for (int index = 0; index < key.Length; index++)
        {
            if (index == 6)
            {
                key[index] = '-';
                continue;
            }

            key[index] = (char)('A' + RandomNumberGenerator.GetInt32(26));
        }

        return new string(key);
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
        return key.Length <= 3 ? key : key[..3] + "…";
    }

    [GeneratedRegex("^[A-Z]{6}-[A-Z]{6}$", RegexOptions.CultureInvariant)]
    private static partial Regex KeyPattern();
}
