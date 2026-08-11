using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Buddy.Core.Abstractions;
using Buddy.Language;

namespace Buddy.App.Services;

public sealed class BuddyFeedbackClient : IDisposable
{
    public const int MaximumMessageCharacters = 3_000;

    public const int MaximumScreenshotBytes = 8 * 1024 * 1024;

    private readonly HttpClient _httpClient;
    private readonly Uri _feedbackEndpoint;
    private readonly ISecretStore _secrets;
    private readonly string? _includedApiKey;
    private readonly bool _ownsClient;

    public BuddyFeedbackClient(
        BuddyProxyClientConfiguration configuration,
        ISecretStore secrets)
        : this(
            configuration?.CreateHttpClient()
                ?? throw new ArgumentNullException(nameof(configuration)),
            new Uri(configuration.Endpoint, "feedback"),
            secrets,
            configuration.IncludedApiKey,
            ownsClient: true)
    {
    }

    internal BuddyFeedbackClient(
        HttpClient httpClient,
        Uri feedbackEndpoint,
        ISecretStore secrets,
        string? includedApiKey,
        bool ownsClient = false)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _feedbackEndpoint = feedbackEndpoint
            ?? throw new ArgumentNullException(nameof(feedbackEndpoint));
        _secrets = secrets ?? throw new ArgumentNullException(nameof(secrets));
        _includedApiKey = includedApiKey;
        _ownsClient = ownsClient;
        if (!_feedbackEndpoint.IsAbsoluteUri
            || _feedbackEndpoint.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException(
                "The feedback endpoint must be an absolute HTTPS URL.",
                nameof(feedbackEndpoint));
        }
    }

    public async Task<FeedbackSubmissionResult> SendAsync(
        string message,
        FeedbackAttachment? screenshot,
        string appVersion,
        string interfaceLanguage,
        string dialogLanguage,
        CancellationToken cancellationToken = default)
    {
        string normalizedMessage = message?.Trim() ?? string.Empty;
        if (normalizedMessage.Length is 0 or > MaximumMessageCharacters)
        {
            throw new ArgumentException(
                $"Feedback must contain between 1 and {MaximumMessageCharacters} characters.",
                nameof(message));
        }

        if (screenshot?.Content.Length > MaximumScreenshotBytes)
        {
            throw new ArgumentException("The screenshot is too large.", nameof(screenshot));
        }

        string? apiKey = await ResolveApiKeyAsync(cancellationToken)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new FeedbackClientException("feedback_auth_missing");
        }

        using MultipartFormDataContent form = new();
        form.Add(Utf8(normalizedMessage), "message");
        form.Add(Utf8(NormalizeMetadata(appVersion)), "app_version");
        form.Add(Utf8(NormalizeMetadata(interfaceLanguage)), "interface_language");
        form.Add(Utf8(NormalizeMetadata(dialogLanguage)), "dialog_language");
        if (screenshot is not null)
        {
            ByteArrayContent image = new(screenshot.Content);
            image.Headers.ContentType = new MediaTypeHeaderValue(screenshot.ContentType);
            form.Add(image, "screenshot", SafeImageName(screenshot));
        }

        using HttpRequestMessage request = new(HttpMethod.Post, _feedbackEndpoint)
        {
            Content = form,
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        using CancellationTokenSource timeout = CancellationTokenSource
            .CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(45));

        HttpResponseMessage response;
        try
        {
            response = await _httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new FeedbackClientException("feedback_timeout");
        }
        catch (HttpRequestException error)
        {
            throw new FeedbackClientException("feedback_unreachable", error);
        }

        using (response)
        {
            JsonDocument? document = null;
            try
            {
                await using Stream stream = await response.Content
                    .ReadAsStreamAsync(timeout.Token)
                    .ConfigureAwait(false);
                document = await JsonDocument.ParseAsync(
                        stream,
                        cancellationToken: timeout.Token)
                    .ConfigureAwait(false);
            }
            catch (JsonException)
            {
                // The stable error below is preferable to displaying upstream HTML.
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new FeedbackClientException("feedback_timeout");
            }
            catch (Exception error) when (error is IOException or HttpRequestException)
            {
                throw new FeedbackClientException("feedback_unreachable", error);
            }

            using (document)
            {
                if (!response.IsSuccessStatusCode)
                {
                    throw new FeedbackClientException(
                        ReadErrorCode(document) ?? "feedback_failed");
                }

                JsonElement root = document?.RootElement
                    ?? throw new FeedbackClientException("feedback_invalid_response");
                if (!root.TryGetProperty("feedback_id", out JsonElement id)
                    || string.IsNullOrWhiteSpace(id.GetString()))
                {
                    throw new FeedbackClientException("feedback_invalid_response");
                }

                bool screenshotDelivered = !root.TryGetProperty(
                        "screenshot_delivered",
                        out JsonElement delivered)
                    || delivered.ValueKind == JsonValueKind.True;
                return new FeedbackSubmissionResult(
                    id.GetString()!,
                    screenshotDelivered);
            }
        }
    }

    public void Dispose()
    {
        if (_ownsClient)
        {
            _httpClient.Dispose();
        }
    }

    private async Task<string?> ResolveApiKeyAsync(
        CancellationToken cancellationToken)
    {
        string? stored = await _secrets
            .GetAsync(BuddyProxyLanguageProvider.SecretKey, cancellationToken)
            .ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(stored))
        {
            return stored.Trim();
        }

        string? environment = Environment.GetEnvironmentVariable(
            BuddyProxyLanguageProvider.EnvironmentVariable);
        return string.IsNullOrWhiteSpace(environment)
            ? _includedApiKey
            : environment.Trim();
    }

    private static StringContent Utf8(string value) => new(
        value,
        Encoding.UTF8,
        "text/plain");

    private static string NormalizeMetadata(string? value)
    {
        string normalized = value?.Trim() ?? string.Empty;
        return normalized.Length <= 100 ? normalized : normalized[..100];
    }

    private static string SafeImageName(FeedbackAttachment screenshot) =>
        screenshot.ContentType switch
        {
            "image/jpeg" => "screenshot.jpg",
            "image/webp" => "screenshot.webp",
            _ => "screenshot.png",
        };

    private static string? ReadErrorCode(JsonDocument? document)
    {
        if (document is null
            || !document.RootElement.TryGetProperty("error", out JsonElement error)
            || !error.TryGetProperty("code", out JsonElement code))
        {
            return null;
        }

        return code.GetString();
    }
}

public sealed record FeedbackAttachment(
    string FileName,
    string ContentType,
    byte[] Content);

public sealed record FeedbackSubmissionResult(
    string FeedbackId,
    bool ScreenshotDelivered);

public sealed class FeedbackClientException : Exception
{
    public FeedbackClientException(string code)
        : base(code)
    {
        Code = code;
    }

    public FeedbackClientException(string code, Exception innerException)
        : base(code, innerException)
    {
        Code = code;
    }

    public string Code { get; }
}
