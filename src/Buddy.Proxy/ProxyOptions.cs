using System.Text.RegularExpressions;

namespace Buddy.Proxy;

public sealed partial class DeepSeekOptions
{
    public const string SectionName = "DeepSeek";

    public string BaseUrl { get; init; } = "https://api.deepseek.com/";

    public string ChatPath { get; init; } = "chat/completions";

    public string ApiKey { get; init; } = string.Empty;

    public int RequestTimeoutSeconds { get; init; } = 180;

    public Uri GetBaseUri()
    {
        if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out Uri? uri)
            || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException(
                "DeepSeek:BaseUrl must be an absolute HTTPS URL.");
        }

        return uri;
    }

    public void Validate()
    {
        _ = GetBaseUri();
        if (string.IsNullOrWhiteSpace(ChatPath)
            || Uri.TryCreate(ChatPath, UriKind.Absolute, out _))
        {
            throw new InvalidOperationException(
                "DeepSeek:ChatPath must be a relative endpoint path.");
        }

        if (!ApiKey.StartsWith("sk-", StringComparison.Ordinal)
            || ApiKey.Length < 16)
        {
            throw new InvalidOperationException(
                "DeepSeek:ApiKey is missing or malformed. Configure it outside source control.");
        }

        if (RequestTimeoutSeconds is < 10 or > 600)
        {
            throw new InvalidOperationException(
                "DeepSeek:RequestTimeoutSeconds must be between 10 and 600.");
        }
    }
}

public sealed partial class ProxyOptions
{
    public const string SectionName = "Proxy";

    public string DatabasePath { get; init; } = "data/buddy-proxy.db";

    public string KeyPepper { get; init; } = string.Empty;

    public int DefaultReplyLimit { get; init; } = 1_000;

    public long DefaultTokenLimit { get; init; } = 1_000_000;

    public int MaximumCompletionTokens { get; init; } = 4_096;

    public int MaximumRequestBytes { get; init; } = 2 * 1024 * 1024;

    public string[] AllowedModels { get; init; } = [];

    public void Validate(string contentRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRoot);
        _ = ResolveDatabasePath(contentRoot);
        if (KeyPepper.Length < 32)
        {
            throw new InvalidOperationException(
                "Proxy:KeyPepper must contain at least 32 characters of deployment-only random data.");
        }

        if (DefaultReplyLimit <= 0 || DefaultTokenLimit <= 0)
        {
            throw new InvalidOperationException("Default quotas must be positive.");
        }

        if (MaximumCompletionTokens is < 64 or > 32_768)
        {
            throw new InvalidOperationException(
                "Proxy:MaximumCompletionTokens must be between 64 and 32768.");
        }

        if (MaximumRequestBytes is < 16_384 or > 16 * 1024 * 1024)
        {
            throw new InvalidOperationException(
                "Proxy:MaximumRequestBytes must be between 16 KiB and 16 MiB.");
        }

        if (AllowedModels.Length == 0
            || AllowedModels.Any(model => !ModelNamePattern().IsMatch(model)))
        {
            throw new InvalidOperationException(
                "Proxy:AllowedModels must contain safe DeepSeek model identifiers.");
        }
    }

    public string ResolveDatabasePath(string contentRoot)
    {
        if (string.IsNullOrWhiteSpace(DatabasePath)
            || Path.IsPathFullyQualified(DatabasePath))
        {
            throw new InvalidOperationException(
                "Proxy:DatabasePath must be relative to the proxy deployment directory.");
        }

        string root = Path.GetFullPath(contentRoot);
        string path = Path.GetFullPath(Path.Combine(root, DatabasePath));
        string prefix = root.TrimEnd(Path.DirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        if (!path.StartsWith(prefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Proxy:DatabasePath must stay inside the proxy deployment directory.");
        }

        return path;
    }

    [GeneratedRegex("^[a-zA-Z0-9._-]{1,128}$", RegexOptions.CultureInvariant)]
    private static partial Regex ModelNamePattern();
}
