using System.Net.Security;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Buddy.App.Services;

public sealed class BuddyProxyClientConfiguration
{
    private readonly byte[]? _certificatePin;

    public BuddyProxyClientConfiguration()
    {
        Endpoint = new Uri(
            Environment.GetEnvironmentVariable("BUDDY_PROXY_ENDPOINT")
                ?? ReadBuildValue("BuddyProxyEndpoint")
                ?? "https://rs.flcl.me:38472/v1/",
            UriKind.Absolute);
        IncludedApiKey = EmptyToNull(
            Environment.GetEnvironmentVariable("BUDDY_PROXY_API_KEY")
                ?? ReadBuildValue("BuddyProxyClientKey"));
        string? pin = EmptyToNull(
            Environment.GetEnvironmentVariable("BUDDY_PROXY_CERT_SHA256")
                ?? ReadBuildValue("BuddyProxyCertSha256"));
        if (pin is not null)
        {
            try
            {
                _certificatePin = Convert.FromHexString(
                    pin.Replace(":", string.Empty, StringComparison.Ordinal));
            }
            catch (FormatException error)
            {
                throw new InvalidOperationException(
                    "The Buddy proxy certificate pin is malformed.",
                    error);
            }

            if (_certificatePin.Length != 32)
            {
                throw new InvalidOperationException(
                    "The Buddy proxy certificate pin must be a SHA-256 hash.");
            }
        }

        if (Endpoint.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("The Buddy proxy endpoint must use HTTPS.");
        }
    }

    public Uri Endpoint { get; }

    public string? IncludedApiKey { get; }

    public bool HasIncludedKey => !string.IsNullOrWhiteSpace(IncludedApiKey);

    public HttpClient CreateHttpClient()
    {
        HttpClientHandler handler = new();
        handler.ServerCertificateCustomValidationCallback = ValidateCertificate;
        return new HttpClient(handler, disposeHandler: true)
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
    }

    private bool ValidateCertificate(
        HttpRequestMessage request,
        X509Certificate2? certificate,
        X509Chain? chain,
        SslPolicyErrors errors)
    {
        _ = chain;
        if (certificate is null
            || request.RequestUri is null
            || !string.Equals(
                request.RequestUri.Host,
                Endpoint.Host,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (_certificatePin is null)
        {
            return errors == SslPolicyErrors.None;
        }

        byte[] actual = certificate.GetCertHash(HashAlgorithmName.SHA256);
        return CryptographicOperations.FixedTimeEquals(actual, _certificatePin);
    }

    private static string? ReadBuildValue(string key)
    {
        return typeof(BuddyProxyClientConfiguration)
            .Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(
                attribute => string.Equals(
                    attribute.Key,
                    key,
                    StringComparison.Ordinal))
            ?.Value;
    }

    private static string? EmptyToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
