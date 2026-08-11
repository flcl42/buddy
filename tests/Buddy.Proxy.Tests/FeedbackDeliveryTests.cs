using System.Net;
using Microsoft.Extensions.Logging.Abstractions;

namespace Buddy.Proxy.Tests;

public sealed class FeedbackDeliveryTests
{
    private const string TestToken =
        "123456789:ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghi";

    [Fact]
    public void ImageDetectionAcceptsOnlySupportedSignatures()
    {
        Assert.Equal(
            "image/png",
            FeedbackLimits.DetectImageContentType(
                new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }));
        Assert.Equal(
            "image/jpeg",
            FeedbackLimits.DetectImageContentType(
                new byte[] { 0xFF, 0xD8, 0xFF, 0x01 }));
        Assert.Equal(
            "image/webp",
            FeedbackLimits.DetectImageContentType("RIFF0000WEBP"u8));
        Assert.Null(FeedbackLimits.DetectImageContentType("not-an-image"u8));
    }

    [Fact]
    public void MetadataIsSingleLineBoundedAndHasAFallback()
    {
        Assert.Equal(
            "version spoofed",
            FeedbackLimits.NormalizeMetadata(" version\r\nspoofed ", "unknown"));
        Assert.Equal(
            "unknown",
            FeedbackLimits.NormalizeMetadata("\r\n\t", "unknown"));
        Assert.Equal(
            FeedbackLimits.MaximumMetadataCharacters,
            FeedbackLimits.NormalizeMetadata(new string('x', 200), "unknown").Length);
    }

    [Fact]
    public void TelegramConfigurationFailsFastOnlyWhenEnabled()
    {
        new TelegramOptions().Validate();

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => new TelegramOptions { Enabled = true }.Validate());

        Assert.Contains("BotToken", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TextAndScreenshotAreSentWithoutLosingTextOnPhotoFailure()
    {
        TrackingHandler handler = new(
            HttpStatusCode.OK,
            HttpStatusCode.InternalServerError);
        using HttpClient httpClient = new(handler)
        {
            BaseAddress = new Uri("https://api.telegram.test/"),
        };
        TelegramOptions options = CreateOptions();
        using TelegramFeedbackGateway gateway = new(
            httpClient,
            options,
            NullLogger<TelegramFeedbackGateway>.Instance);

        FeedbackDeliveryResult result = await gateway.DeliverAsync(
            CreateSubmission(
                new FeedbackScreenshot(
                    new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A },
                    "image/png")),
            CreateClient());

        Assert.False(result.ScreenshotDelivered);
        Assert.Collection(
            handler.Requests,
            request =>
            {
                Assert.EndsWith("/sendMessage", request.Path, StringComparison.Ordinal);
                Assert.Contains("Clear feedback text", request.Body, StringComparison.Ordinal);
            },
            request =>
            {
                Assert.EndsWith("/sendPhoto", request.Path, StringComparison.Ordinal);
                Assert.StartsWith("multipart/form-data", request.ContentType, StringComparison.Ordinal);
            });
    }

    [Fact]
    public async Task MessageFailureIsReportedAsDeliveryFailure()
    {
        TrackingHandler handler = new(HttpStatusCode.BadGateway);
        using HttpClient httpClient = new(handler)
        {
            BaseAddress = new Uri("https://api.telegram.test/"),
        };
        using TelegramFeedbackGateway gateway = new(
            httpClient,
            CreateOptions(),
            NullLogger<TelegramFeedbackGateway>.Instance);

        await Assert.ThrowsAsync<FeedbackDeliveryException>(
            () => gateway.DeliverAsync(CreateSubmission(), CreateClient()));
    }

    [Fact]
    public void MaximumFeedbackStillFitsTelegramTextMessage()
    {
        FeedbackSubmission submission = CreateSubmission() with
        {
            Message = new string('x', FeedbackLimits.MaximumMessageCharacters),
        };

        string text = TelegramFeedbackGateway.CreateMessage(
            submission,
            CreateClient());

        Assert.True(text.Length <= 4_096, $"Telegram message length was {text.Length}.");
    }

    private static TelegramOptions CreateOptions() => new()
    {
        Enabled = true,
        ApiBaseUrl = "https://api.telegram.test/",
        BotToken = TestToken,
        ChatId = "123456789",
    };

    private static FeedbackSubmission CreateSubmission(
        FeedbackScreenshot? screenshot = null) => new(
        "FB-20260811-ABCDEF12",
        "Clear feedback text",
        "0.2.0",
        "en",
        "en",
        new DateTimeOffset(2026, 8, 11, 12, 0, 0, TimeSpan.Zero),
        screenshot);

    private static ProxyClient CreateClient() => new(
        42,
        "test-client",
        "ABCDEF",
        ProxyKeyState.Active,
        1_000,
        1_000_000,
        0,
        0,
        0,
        DateTimeOffset.UtcNow,
        null);

    private sealed class TrackingHandler(params HttpStatusCode[] statuses)
        : HttpMessageHandler
    {
        private readonly Queue<HttpStatusCode> _statuses = new(statuses);

        public List<CapturedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(
                new CapturedRequest(
                    request.RequestUri!.AbsolutePath,
                    request.Content?.Headers.ContentType?.MediaType ?? string.Empty,
                    body));
            HttpStatusCode status = _statuses.Count > 0
                ? _statuses.Dequeue()
                : HttpStatusCode.OK;
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(
                    status == HttpStatusCode.OK
                        ? "{\"ok\":true,\"result\":{}}"
                        : "{\"ok\":false}"),
            };
        }
    }

    private sealed record CapturedRequest(
        string Path,
        string ContentType,
        string Body);
}
