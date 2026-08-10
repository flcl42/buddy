using Buddy.Core.Abstractions;
using Buddy.Core.Domain;

namespace Buddy.Language;

public sealed class BuddyProxyLanguageProvider :
    ILanguageImprovementProvider,
    IConversationProvider,
    IWordDefinitionProvider
{
    public const string ProviderIdValue = "buddy-proxy";
    public const string SecretKey = "provider.buddy-proxy.api-key";
    public const string EnvironmentVariable = "BUDDY_PROXY_API_KEY";
    public const string DefaultModel = "deepseek-v4-flash";

    private readonly StructuredLanguageProvider _inner;

    public BuddyProxyLanguageProvider(
        HttpClient httpClient,
        ISecretStore secrets,
        Uri baseAddress,
        string? includedApiKey = null,
        string model = DefaultModel)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(secrets);
        ArgumentNullException.ThrowIfNull(baseAddress);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        if (!baseAddress.IsAbsoluteUri || baseAddress.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException(
                "The Buddy proxy endpoint must be an absolute HTTPS URL.",
                nameof(baseAddress));
        }

        _inner = new StructuredLanguageProvider(
            httpClient,
            secrets,
            new StructuredLanguageProviderOptions(
                ProviderIdValue,
                "Buddy free DeepSeek",
                model,
                new Uri(baseAddress, "chat/completions"),
                new Uri(baseAddress, "models"),
                TimeSpan.FromMinutes(3),
                MaximumConversationCharacters: 400_000,
                RequiresApiKey: true,
                SecretKey: SecretKey,
                EnvironmentVariable: EnvironmentVariable,
                FallbackApiKey: includedApiKey,
                SendThinkingDisabled: true)
                .Validate());
    }

    public string ProviderId => ProviderIdValue;

    public Task<ImprovementResult> ImproveAsync(
        ImprovementRequest request,
        CancellationToken cancellationToken = default) =>
        _inner.ImproveAsync(request, cancellationToken);

    public Task<TitleResult> CreateTitleAsync(
        TitleRequest request,
        CancellationToken cancellationToken = default) =>
        _inner.CreateTitleAsync(request, cancellationToken);

    public Task<ConversationResult> RespondAsync(
        ConversationRequest request,
        CancellationToken cancellationToken = default) =>
        _inner.RespondAsync(request, cancellationToken);

    public Task<WordDefinitionResult> DefineAsync(
        WordDefinitionRequest request,
        CancellationToken cancellationToken = default) =>
        _inner.DefineAsync(request, cancellationToken);

    public Task<ProviderHealth> CheckHealthAsync(
        CancellationToken cancellationToken = default) =>
        _inner.CheckHealthAsync(cancellationToken);
}
