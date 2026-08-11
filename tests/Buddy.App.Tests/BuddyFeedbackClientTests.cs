using System.Net;
using Buddy.App.Services;
using Buddy.Core.Abstractions;

namespace Buddy.App.Tests;

public sealed class BuddyFeedbackClientTests
{
    [Fact]
    public async Task SendsAuthenticatedMultipartFeedbackWithOptionalImage()
    {
        CapturingHandler handler = new(
            HttpStatusCode.OK,
            "{\"feedback_id\":\"FB-TEST\",\"screenshot_delivered\":true}");
        using HttpClient httpClient = new(handler);
        using BuddyFeedbackClient client = new(
            httpClient,
            new Uri("https://feedback.example/v1/feedback"),
            new MemorySecretStore("ABCDEF-GHIJKL"),
            includedApiKey: "ZZZZZZ-YYYYYY");
        FeedbackAttachment screenshot = new(
            "private-name.png",
            "image/png",
            new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });

        FeedbackSubmissionResult result = await client.SendAsync(
            "The feedback form works.",
            screenshot,
            "0.2.0",
            "en",
            "de");

        Assert.Equal("FB-TEST", result.FeedbackId);
        Assert.True(result.ScreenshotDelivered);
        Assert.Equal("Bearer", handler.AuthorizationScheme);
        Assert.Equal("ABCDEF-GHIJKL", handler.AuthorizationParameter);
        Assert.StartsWith("multipart/form-data", handler.ContentType, StringComparison.Ordinal);
        Assert.Contains("The feedback form works.", handler.Body, StringComparison.Ordinal);
        Assert.Contains("image/png", handler.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("private-name.png", handler.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StableProxyErrorCodeIsPreserved()
    {
        CapturingHandler handler = new(
            HttpStatusCode.ServiceUnavailable,
            "{\"error\":{\"code\":\"proxy_feedback_unavailable\"}}");
        using HttpClient httpClient = new(handler);
        using BuddyFeedbackClient client = new(
            httpClient,
            new Uri("https://feedback.example/v1/feedback"),
            new MemorySecretStore("ABCDEF-GHIJKL"),
            includedApiKey: null);

        FeedbackClientException error = await Assert.ThrowsAsync<FeedbackClientException>(
            () => client.SendAsync("Please improve this.", null, "0.2.0", "en", "en"));

        Assert.Equal("proxy_feedback_unavailable", error.Code);
    }

    [Fact]
    public async Task MalformedSuccessResponseUsesStableClientError()
    {
        CapturingHandler handler = new(HttpStatusCode.OK, "<html>not json</html>");
        using HttpClient httpClient = new(handler);
        using BuddyFeedbackClient client = new(
            httpClient,
            new Uri("https://feedback.example/v1/feedback"),
            new MemorySecretStore("ABCDEF-GHIJKL"),
            includedApiKey: null);

        FeedbackClientException error = await Assert.ThrowsAsync<FeedbackClientException>(
            () => client.SendAsync("Please improve this.", null, "0.2.0", "en", "en"));

        Assert.Equal("feedback_invalid_response", error.Code);
    }

    [Fact]
    public async Task MissingProxyCodeFailsBeforeNetworkRequest()
    {
        CapturingHandler handler = new(HttpStatusCode.OK, "{}");
        using HttpClient httpClient = new(handler);
        using BuddyFeedbackClient client = new(
            httpClient,
            new Uri("https://feedback.example/v1/feedback"),
            new MemorySecretStore(null),
            includedApiKey: null);

        FeedbackClientException error = await Assert.ThrowsAsync<FeedbackClientException>(
            () => client.SendAsync("Please improve this.", null, "0.2.0", "en", "en"));

        Assert.Equal("feedback_auth_missing", error.Code);
        Assert.Equal(0, handler.RequestCount);
    }

    private sealed class CapturingHandler(
        HttpStatusCode status,
        string responseBody) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        public string? AuthorizationScheme { get; private set; }

        public string? AuthorizationParameter { get; private set; }

        public string ContentType { get; private set; } = string.Empty;

        public string Body { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            AuthorizationScheme = request.Headers.Authorization?.Scheme;
            AuthorizationParameter = request.Headers.Authorization?.Parameter;
            ContentType = request.Content?.Headers.ContentType?.MediaType ?? string.Empty;
            Body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(responseBody),
            };
        }
    }

    private sealed class MemorySecretStore(string? value) : ISecretStore
    {
        public Task<string?> GetAsync(
            string key,
            CancellationToken cancellationToken = default)
        {
            _ = key;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(value);
        }

        public Task SetAsync(
            string key,
            string value,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task RemoveAsync(
            string key,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
