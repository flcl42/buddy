using System.Text.RegularExpressions;

namespace Buddy.Proxy;

public sealed partial class TelegramOptions
{
    public const string SectionName = "Telegram";

    public bool Enabled { get; init; }

    public string ApiBaseUrl { get; init; } = "https://api.telegram.org/";

    public string BotToken { get; init; } = string.Empty;

    public string ChatId { get; init; } = string.Empty;

    public int RequestTimeoutSeconds { get; init; } = 30;

    public Uri GetBaseUri()
    {
        if (!Uri.TryCreate(ApiBaseUrl, UriKind.Absolute, out Uri? uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new InvalidOperationException(
                "Telegram:ApiBaseUrl must be an absolute HTTPS URL without a query or fragment.");
        }

        return uri;
    }

    public void Validate()
    {
        _ = GetBaseUri();
        if (RequestTimeoutSeconds is < 5 or > 120)
        {
            throw new InvalidOperationException(
                "Telegram:RequestTimeoutSeconds must be between 5 and 120.");
        }

        if (!Enabled)
        {
            return;
        }

        if (!BotTokenPattern().IsMatch(BotToken))
        {
            throw new InvalidOperationException(
                "Telegram:BotToken is missing or malformed. Configure it outside source control.");
        }

        if (!ChatIdPattern().IsMatch(ChatId))
        {
            throw new InvalidOperationException(
                "Telegram:ChatId must be a numeric chat identifier or channel username.");
        }
    }

    [GeneratedRegex("^[0-9]{6,12}:[A-Za-z0-9_-]{30,80}$", RegexOptions.CultureInvariant)]
    private static partial Regex BotTokenPattern();

    [GeneratedRegex("^(?:-?[0-9]{1,20}|@[A-Za-z][A-Za-z0-9_]{4,31})$", RegexOptions.CultureInvariant)]
    private static partial Regex ChatIdPattern();
}
