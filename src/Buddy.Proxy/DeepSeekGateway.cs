using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Buddy.Proxy;

public sealed record GatewaySuccess(
    byte[] Body,
    string Model,
    int PromptTokens,
    int CompletionTokens,
    bool CountsAsReply);

public sealed record GatewayFailure(
    int StatusCode,
    string Code,
    string Message,
    TimeSpan? RetryAfter = null);

public sealed record GatewayResult(
    GatewaySuccess? Success,
    GatewayFailure? Failure)
{
    public static GatewayResult FromSuccess(GatewaySuccess success) =>
        new(success, null);

    public static GatewayResult FromFailure(GatewayFailure failure) =>
        new(null, failure);
}

public sealed class DeepSeekGateway
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly DeepSeekOptions _deepSeek;

    public DeepSeekGateway(HttpClient httpClient, DeepSeekOptions deepSeek)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _deepSeek = deepSeek ?? throw new ArgumentNullException(nameof(deepSeek));
    }

    public async Task<GatewayResult> SendAsync(
        JsonObject payload,
        string requestId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
        using HttpRequestMessage request = new(
            HttpMethod.Post,
            _deepSeek.ChatPath);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            _deepSeek.ApiKey);
        request.Headers.TryAddWithoutValidation("X-Request-ID", requestId);
        request.Content = new ByteArrayContent(body);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json")
        {
            CharSet = Encoding.UTF8.WebName,
        };

        using CancellationTokenSource timeout =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(_deepSeek.RequestTimeoutSeconds));
        HttpResponseMessage response;
        try
        {
            response = await _httpClient
                .SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    timeout.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (
            timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return GatewayResult.FromFailure(
                new GatewayFailure(
                    StatusCodes.Status504GatewayTimeout,
                    ProxyErrorCodes.UpstreamUnavailable,
                    "DeepSeek did not respond before the proxy timeout."));
        }
        catch (HttpRequestException)
        {
            return GatewayResult.FromFailure(
                new GatewayFailure(
                    StatusCodes.Status503ServiceUnavailable,
                    ProxyErrorCodes.UpstreamUnavailable,
                    "DeepSeek is currently unreachable."));
        }

        using (response)
        {
            byte[] responseBody = await response.Content
                .ReadAsByteArrayAsync(cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return GatewayResult.FromFailure(MapUpstreamFailure(response));
            }

            try
            {
                using JsonDocument json = JsonDocument.Parse(responseBody);
                JsonElement root = json.RootElement;
                JsonElement usage = root.GetProperty("usage");
                int promptTokens = usage.GetProperty("prompt_tokens").GetInt32();
                int completionTokens = usage.GetProperty("completion_tokens").GetInt32();
                string model = root.TryGetProperty("model", out JsonElement modelElement)
                    ? modelElement.GetString() ?? "unknown"
                    : "unknown";
                bool countsAsReply = root.TryGetProperty(
                        "choices",
                        out JsonElement choices)
                    && choices.ValueKind == JsonValueKind.Array
                    && choices.GetArrayLength() > 0;
                if (promptTokens < 0 || completionTokens < 0 || !countsAsReply)
                {
                    throw new JsonException("The usage or choices payload was invalid.");
                }

                return GatewayResult.FromSuccess(
                    new GatewaySuccess(
                        responseBody,
                        model,
                        promptTokens,
                        completionTokens,
                        countsAsReply));
            }
            catch (Exception error) when (
                error is JsonException
                    or InvalidOperationException
                    or KeyNotFoundException)
            {
                return GatewayResult.FromFailure(
                    new GatewayFailure(
                        StatusCodes.Status502BadGateway,
                        ProxyErrorCodes.UpstreamProtocol,
                        "DeepSeek returned a response without billable usage data."));
            }
        }
    }

    private static GatewayFailure MapUpstreamFailure(HttpResponseMessage response)
    {
        TimeSpan? retryAfter = response.Headers.RetryAfter?.Delta;
        return response.StatusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                new GatewayFailure(
                    StatusCodes.Status502BadGateway,
                    ProxyErrorCodes.UpstreamAuthentication,
                    "The proxy's DeepSeek credential was rejected."),
            HttpStatusCode.PaymentRequired =>
                new GatewayFailure(
                    StatusCodes.Status503ServiceUnavailable,
                    ProxyErrorCodes.UpstreamBalance,
                    "The proxy's DeepSeek balance is unavailable."),
            HttpStatusCode.TooManyRequests =>
                new GatewayFailure(
                    StatusCodes.Status503ServiceUnavailable,
                    ProxyErrorCodes.UpstreamUnavailable,
                    "DeepSeek is rate-limiting the proxy.",
                    retryAfter),
            _ when (int)response.StatusCode >= 500 =>
                new GatewayFailure(
                    StatusCodes.Status503ServiceUnavailable,
                    ProxyErrorCodes.UpstreamUnavailable,
                    "DeepSeek is temporarily unavailable.",
                    retryAfter),
            _ => new GatewayFailure(
                StatusCodes.Status502BadGateway,
                ProxyErrorCodes.UpstreamProtocol,
                $"DeepSeek rejected the forwarded request with HTTP {(int)response.StatusCode}."),
        };
    }
}
