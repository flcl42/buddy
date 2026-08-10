using System.Text.Json.Nodes;

namespace Buddy.Proxy.Tests;

public sealed class ProxyEndpointPolicyTests
{
    [Fact]
    public void InputReservationIsConservativeAndIgnoresRequestedOutputLimit()
    {
        JsonObject first = new()
        {
            ["model"] = "deepseek-v4-flash",
            ["messages"] = new JsonArray(
                new JsonObject
                {
                    ["role"] = "user",
                    ["content"] = "A short request.",
                }),
            ["max_tokens"] = 128,
        };
        JsonObject second = (JsonObject)first.DeepClone();
        second["max_tokens"] = 4_096;

        int reservation = ProxyEndpoints.EstimateInputReservation(first);

        Assert.Equal(reservation, ProxyEndpoints.EstimateInputReservation(second));
        Assert.True(reservation > 512);
    }

    [Theory]
    [InlineData(ProxyErrorCodes.InvalidKey, "proxy_key_invalid")]
    [InlineData(ProxyErrorCodes.DisabledKey, "proxy_key_disabled")]
    [InlineData(
        ProxyErrorCodes.ReplyQuotaExhausted,
        "proxy_reply_quota_exhausted")]
    [InlineData(
        ProxyErrorCodes.TokenQuotaExhausted,
        "proxy_token_quota_exhausted")]
    public void KeyAndQuotaErrorsAreStable(string actual, string expected)
    {
        Assert.Equal(expected, actual);
    }
}
