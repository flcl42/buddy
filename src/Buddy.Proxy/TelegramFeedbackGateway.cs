using System.Net.Http.Json;
using System.Text.Json;

namespace Buddy.Proxy;

public sealed partial class TelegramFeedbackGateway : IDisposable
{
    private readonly TelegramOptions _options;
    private readonly ILogger<TelegramFeedbackGateway> _logger;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsClient;

    public TelegramFeedbackGateway(
        TelegramOptions options,
        ILogger<TelegramFeedbackGateway> logger)
        : this(CreateHttpClient(options), options, logger, ownsClient: true)
    {
    }

    internal TelegramFeedbackGateway(
        HttpClient httpClient,
        TelegramOptions options,
        ILogger<TelegramFeedbackGateway> logger,
        bool ownsClient = false)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _ownsClient = ownsClient;
    }

    public async Task<FeedbackDeliveryResult> DeliverAsync(
        FeedbackSubmission submission,
        ProxyClient client,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(submission);
        ArgumentNullException.ThrowIfNull(client);
        if (!_options.Enabled)
        {
            throw new FeedbackDeliveryUnavailableException();
        }

        string text = CreateMessage(submission, client);
        if (!await SendMessageAsync(text, cancellationToken).ConfigureAwait(false))
        {
            throw new FeedbackDeliveryException();
        }

        bool screenshotDelivered = submission.Screenshot is null
            || await SendScreenshotAsync(submission, cancellationToken)
                .ConfigureAwait(false);
        if (!screenshotDelivered)
        {
            LogScreenshotMissing(_logger, submission.Id);
        }

        return new FeedbackDeliveryResult(submission.Id, screenshotDelivered);
    }

    public void Dispose()
    {
        if (_ownsClient)
        {
            _httpClient.Dispose();
        }
    }

    internal static string CreateMessage(
        FeedbackSubmission submission,
        ProxyClient client) =>
        string.Join(
            '\n',
            "💬 Chitchat Buddy feedback",
            $"ID: {submission.Id}",
            $"UTC: {submission.SubmittedUtc:yyyy-MM-dd HH:mm:ss}",
            $"App: {submission.AppVersion}",
            $"Interface: {submission.InterfaceLanguage}",
            $"Dialog: {submission.DialogLanguage}",
            $"Client: {client.KeyPrefix}",
            string.Empty,
            submission.Message);

    private static HttpClient CreateHttpClient(TelegramOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        SocketsHttpHandler handler = new()
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
        };
        return new HttpClient(handler, disposeHandler: true)
        {
            BaseAddress = options.GetBaseUri(),
            Timeout = TimeSpan.FromSeconds(options.RequestTimeoutSeconds),
        };
    }

    private async Task<bool> SendMessageAsync(
        string text,
        CancellationToken cancellationToken)
    {
        try
        {
            using HttpResponseMessage response = await _httpClient.PostAsJsonAsync(
                    MethodUri("sendMessage"),
                    new
                    {
                        chat_id = _options.ChatId,
                        text,
                        disable_web_page_preview = true,
                    },
                    cancellationToken)
                .ConfigureAwait(false);
            return await IsSuccessfulAsync(response, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            LogTelegramRequestFailure(_logger);
            return false;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            LogTelegramTimeout(_logger);
            return false;
        }
    }

    private async Task<bool> SendScreenshotAsync(
        FeedbackSubmission submission,
        CancellationToken cancellationToken)
    {
        FeedbackScreenshot screenshot = submission.Screenshot!;
        using MultipartFormDataContent form = new();
        form.Add(new StringContent(_options.ChatId), "chat_id");
        form.Add(
            new StringContent($"Screenshot for feedback {submission.Id}"),
            "caption");
        ByteArrayContent photo = new(screenshot.Content);
        photo.Headers.ContentType = new(screenshot.ContentType);
        form.Add(photo, "photo", ScreenshotFileName(screenshot.ContentType));

        try
        {
            using HttpResponseMessage response = await _httpClient.PostAsync(
                    MethodUri("sendPhoto"),
                    form,
                    cancellationToken)
                .ConfigureAwait(false);
            return await IsSuccessfulAsync(response, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            LogScreenshotRequestFailure(_logger, submission.Id);
            return false;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            LogScreenshotTimeout(_logger, submission.Id);
            return false;
        }
    }

    private async Task<bool> IsSuccessfulAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            LogTelegramStatusFailure(_logger, (int)response.StatusCode);
            return false;
        }

        try
        {
            await using Stream stream = await response.Content
                .ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            using JsonDocument document = await JsonDocument.ParseAsync(
                    stream,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return document.RootElement.TryGetProperty("ok", out JsonElement ok)
                && ok.ValueKind == JsonValueKind.True;
        }
        catch (Exception error) when (error is JsonException or IOException)
        {
            LogMalformedTelegramResponse(_logger);
            return false;
        }
    }

    private Uri MethodUri(string method) => new(
        _httpClient.BaseAddress
            ?? throw new InvalidOperationException("Telegram base address is missing."),
        $"./bot{_options.BotToken}/{method}");

    private static string ScreenshotFileName(string contentType) => contentType switch
    {
        "image/jpeg" => "screenshot.jpg",
        "image/webp" => "screenshot.webp",
        _ => "screenshot.png",
    };

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Warning,
        Message = "Feedback {FeedbackId} was delivered without its screenshot.")]
    private static partial void LogScreenshotMissing(
        ILogger logger,
        string feedbackId);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Warning,
        Message = "Telegram feedback delivery could not connect.")]
    private static partial void LogTelegramRequestFailure(
        ILogger logger);

    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Warning,
        Message = "Telegram feedback delivery timed out.")]
    private static partial void LogTelegramTimeout(ILogger logger);

    [LoggerMessage(
        EventId = 4,
        Level = LogLevel.Warning,
        Message = "Telegram rejected the screenshot for feedback {FeedbackId}.")]
    private static partial void LogScreenshotRequestFailure(
        ILogger logger,
        string feedbackId);

    [LoggerMessage(
        EventId = 5,
        Level = LogLevel.Warning,
        Message = "Telegram screenshot delivery timed out for feedback {FeedbackId}.")]
    private static partial void LogScreenshotTimeout(
        ILogger logger,
        string feedbackId);

    [LoggerMessage(
        EventId = 6,
        Level = LogLevel.Warning,
        Message = "Telegram feedback request failed with HTTP {StatusCode}.")]
    private static partial void LogTelegramStatusFailure(
        ILogger logger,
        int statusCode);

    [LoggerMessage(
        EventId = 7,
        Level = LogLevel.Warning,
        Message = "Telegram returned a malformed feedback response.")]
    private static partial void LogMalformedTelegramResponse(ILogger logger);
}

public sealed class FeedbackDeliveryUnavailableException : Exception
{
    public FeedbackDeliveryUnavailableException()
        : base("Feedback delivery is not configured.")
    {
    }
}

public sealed class FeedbackDeliveryException : Exception
{
    public FeedbackDeliveryException()
        : base("Feedback could not be delivered.")
    {
    }
}
