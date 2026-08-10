namespace Buddy.Language;

internal sealed record StructuredLanguageProviderOptions(
    string ProviderId,
    string DisplayName,
    string Model,
    Uri ChatEndpoint,
    Uri ModelsEndpoint,
    TimeSpan RequestTimeout,
    int MaximumConversationCharacters,
    bool RequiresApiKey = false,
    string? SecretKey = null,
    string? EnvironmentVariable = null,
    string? StaticApiKey = null,
    string? FallbackApiKey = null,
    bool SendThinkingDisabled = false,
    int? TopK = null,
    double? TopP = null,
    double? PresencePenalty = null)
{
    public StructuredLanguageProviderOptions Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ProviderId);
        ArgumentException.ThrowIfNullOrWhiteSpace(DisplayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(Model);
        ArgumentNullException.ThrowIfNull(ChatEndpoint);
        ArgumentNullException.ThrowIfNull(ModelsEndpoint);
        if (!ChatEndpoint.IsAbsoluteUri || !ModelsEndpoint.IsAbsoluteUri)
        {
            throw new ArgumentException("Provider endpoints must be absolute URIs.");
        }

        if (RequestTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(RequestTimeout),
                RequestTimeout,
                "The request timeout must be positive.");
        }

        if (MaximumConversationCharacters < 10_000)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaximumConversationCharacters),
                MaximumConversationCharacters,
                "The conversation limit must be at least 10,000 characters.");
        }

        if (RequiresApiKey
            && (string.IsNullOrWhiteSpace(SecretKey)
                || string.IsNullOrWhiteSpace(EnvironmentVariable)))
        {
            throw new ArgumentException(
                "An authenticated provider requires both a secret key name and environment variable.");
        }

        return this;
    }
}
