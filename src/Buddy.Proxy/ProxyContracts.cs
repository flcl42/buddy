using System.Text.Json.Serialization;

namespace Buddy.Proxy;

public enum ProxyKeyState
{
    Active = 0,
    Disabled = 1,
}

public sealed record ProxyClient(
    long Id,
    string Name,
    string KeyPrefix,
    ProxyKeyState State,
    int ReplyLimit,
    long TokenLimit,
    int RepliesUsed,
    long PromptTokensUsed,
    long CompletionTokensUsed,
    DateTimeOffset CreatedUtc,
    DateTimeOffset? LastUsedUtc)
{
    public long TokensUsed => PromptTokensUsed + CompletionTokensUsed;

    public int RepliesRemaining => Math.Max(0, ReplyLimit - RepliesUsed);

    public long TokensRemaining => Math.Max(0, TokenLimit - TokensUsed);
}

public sealed record ProxyUsage(
    string RequestId,
    string Model,
    int PromptTokens,
    int CompletionTokens,
    bool CountsAsReply);

public sealed record ProxyQuotaResponse(
    string KeyPrefix,
    string State,
    [property: JsonPropertyName("reply_limit")] int ReplyLimit,
    [property: JsonPropertyName("replies_used")] int RepliesUsed,
    [property: JsonPropertyName("replies_remaining")] int RepliesRemaining,
    [property: JsonPropertyName("token_limit")] long TokenLimit,
    [property: JsonPropertyName("prompt_tokens_used")] long PromptTokensUsed,
    [property: JsonPropertyName("completion_tokens_used")] long CompletionTokensUsed,
    [property: JsonPropertyName("tokens_used")] long TokensUsed,
    [property: JsonPropertyName("tokens_remaining")] long TokensRemaining)
{
    public static ProxyQuotaResponse FromClient(ProxyClient client) =>
        new(
            client.KeyPrefix,
            client.State.ToString().ToLowerInvariant(),
            client.ReplyLimit,
            client.RepliesUsed,
            client.RepliesRemaining,
            client.TokenLimit,
            client.PromptTokensUsed,
            client.CompletionTokensUsed,
            client.TokensUsed,
            client.TokensRemaining);
}

public static class ProxyErrorCodes
{
    public const string InvalidKey = "proxy_key_invalid";
    public const string DisabledKey = "proxy_key_disabled";
    public const string ReplyQuotaExhausted = "proxy_reply_quota_exhausted";
    public const string TokenQuotaExhausted = "proxy_token_quota_exhausted";
    public const string RateLimited = "proxy_rate_limited";
    public const string InvalidRequest = "proxy_invalid_request";
    public const string StreamingUnsupported = "proxy_streaming_unsupported";
    public const string ModelUnavailable = "proxy_model_unavailable";
    public const string UpstreamAuthentication = "proxy_upstream_authentication_failed";
    public const string UpstreamBalance = "proxy_upstream_balance_exhausted";
    public const string UpstreamUnavailable = "proxy_upstream_unavailable";
    public const string UpstreamProtocol = "proxy_upstream_protocol_error";
    public const string FeedbackInvalid = "proxy_feedback_invalid";
    public const string FeedbackUnavailable = "proxy_feedback_unavailable";
    public const string FeedbackDeliveryFailed = "proxy_feedback_delivery_failed";
}
