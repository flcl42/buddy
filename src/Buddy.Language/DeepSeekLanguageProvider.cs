using Buddy.Core.Abstractions;
using Buddy.Core.Domain;

namespace Buddy.Language;

public sealed class DeepSeekLanguageProvider :
    ILanguageImprovementProvider,
    IConversationProvider,
    IWordDefinitionProvider
{
    public const string ProviderIdValue = "deepseek";
    public const string SecretKey = "provider.deepseek.api-key";
    public const string DefaultModel = "deepseek-v4-flash";

    private static readonly StructuredLanguageProviderOptions DefaultOptions = new(
        ProviderIdValue,
        "DeepSeek",
        DefaultModel,
        new Uri("https://api.deepseek.com/chat/completions"),
        new Uri("https://api.deepseek.com/models"),
        TimeSpan.FromSeconds(60),
        MaximumConversationCharacters: 400_000,
        RequiresApiKey: true,
        SecretKey: SecretKey,
        EnvironmentVariable: "DEEPSEEK_API_KEY",
        SendThinkingDisabled: true);

    private readonly StructuredLanguageProvider _inner;

    public DeepSeekLanguageProvider(
        HttpClient httpClient,
        ISecretStore secrets,
        string model = DefaultModel)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        _inner = new StructuredLanguageProvider(
            httpClient,
            secrets,
            (DefaultOptions with
            {
                Model = model,
            }).Validate());
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
