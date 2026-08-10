using Buddy.Core.Abstractions;
using Buddy.Core.Domain;

namespace Buddy.Language;

public sealed record LanguageProviderChoice(
    string ProviderId,
    string DisplayName,
    string Description);

public sealed class LanguageProviderRouter :
    ILanguageImprovementProvider,
    IConversationProvider,
    IWordDefinitionProvider
{
    public const string DefaultProviderId = BuddyProxyLanguageProvider.ProviderIdValue;

    private static readonly IReadOnlyList<LanguageProviderChoice> AvailableChoices =
    [
        new(
            BuddyProxyLanguageProvider.ProviderIdValue,
            "Buddy free DeepSeek · included (default)",
            "Cloud DeepSeek through Buddy's limited-access gateway: 1,000 replies or 1,000,000 tokens per included key."),
        new(
            DeepSeekLanguageProvider.ProviderIdValue,
            "DeepSeek V4 Flash · your API key",
            "Direct cloud inference billed to your own DeepSeek account."),
        new(
            QwenLanguageProvider.ProviderIdValue,
            "Qwen 3.6 27B · local accelerated",
            "Private local inference with DFlash acceleration; required files are installed when this option is activated."),
    ];

    private readonly BuddyProxyLanguageProvider _proxy;
    private readonly QwenLanguageProvider _qwen;
    private readonly DeepSeekLanguageProvider _deepSeek;
    private readonly IAppSettingsStore _settings;
    private readonly IReadOnlyList<LanguageProviderChoice> _choices = AvailableChoices;
    private string _selectedProviderId = DefaultProviderId;

    public LanguageProviderRouter(
        BuddyProxyLanguageProvider proxy,
        QwenLanguageProvider qwen,
        DeepSeekLanguageProvider deepSeek,
        IAppSettingsStore settings)
    {
        _proxy = proxy ?? throw new ArgumentNullException(nameof(proxy));
        _qwen = qwen ?? throw new ArgumentNullException(nameof(qwen));
        _deepSeek = deepSeek ?? throw new ArgumentNullException(nameof(deepSeek));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public IReadOnlyList<LanguageProviderChoice> Choices => _choices;

    public string ProviderId => Volatile.Read(ref _selectedProviderId);

    public LanguageProviderChoice SelectedChoice => AvailableChoices.Single(
        choice => string.Equals(
            choice.ProviderId,
            ProviderId,
            StringComparison.Ordinal));

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        string? saved = await _settings
            .GetAsync(BuddySettings.LanguageProviderId, cancellationToken)
            .ConfigureAwait(false);
        string selected = IsSupported(saved) ? saved! : DefaultProviderId;
        Volatile.Write(ref _selectedProviderId, selected);
    }

    public async Task SelectAsync(
        string providerId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        if (!IsSupported(providerId))
        {
            throw new ArgumentOutOfRangeException(
                nameof(providerId),
                providerId,
                "Unknown language provider.");
        }

        await _settings
            .SetAsync(BuddySettings.LanguageProviderId, providerId, cancellationToken)
            .ConfigureAwait(false);
        Volatile.Write(ref _selectedProviderId, providerId);
    }

    public Task<ImprovementResult> ImproveAsync(
        ImprovementRequest request,
        CancellationToken cancellationToken = default) =>
        ImprovementProvider.ImproveAsync(request, cancellationToken);

    public Task<TitleResult> CreateTitleAsync(
        TitleRequest request,
        CancellationToken cancellationToken = default) =>
        ImprovementProvider.CreateTitleAsync(request, cancellationToken);

    public Task<ConversationResult> RespondAsync(
        ConversationRequest request,
        CancellationToken cancellationToken = default) =>
        ConversationProvider.RespondAsync(request, cancellationToken);

    public Task<WordDefinitionResult> DefineAsync(
        WordDefinitionRequest request,
        CancellationToken cancellationToken = default) =>
        WordDefinitionProvider.DefineAsync(request, cancellationToken);

    public Task<ProviderHealth> CheckHealthAsync(
        CancellationToken cancellationToken = default) =>
        ImprovementProvider.CheckHealthAsync(cancellationToken);

    private ILanguageImprovementProvider ImprovementProvider =>
        ProviderId switch
        {
            BuddyProxyLanguageProvider.ProviderIdValue => _proxy,
            QwenLanguageProvider.ProviderIdValue => _qwen,
            DeepSeekLanguageProvider.ProviderIdValue => _deepSeek,
            _ => throw new InvalidOperationException("The selected provider is unavailable."),
        };

    private IConversationProvider ConversationProvider =>
        ProviderId switch
        {
            BuddyProxyLanguageProvider.ProviderIdValue => _proxy,
            QwenLanguageProvider.ProviderIdValue => _qwen,
            DeepSeekLanguageProvider.ProviderIdValue => _deepSeek,
            _ => throw new InvalidOperationException("The selected provider is unavailable."),
        };

    private IWordDefinitionProvider WordDefinitionProvider =>
        ProviderId switch
        {
            BuddyProxyLanguageProvider.ProviderIdValue => _proxy,
            QwenLanguageProvider.ProviderIdValue => _qwen,
            DeepSeekLanguageProvider.ProviderIdValue => _deepSeek,
            _ => throw new InvalidOperationException("The selected provider is unavailable."),
        };

    private static bool IsSupported(string? providerId) =>
        AvailableChoices.Any(choice => string.Equals(
            choice.ProviderId,
            providerId,
            StringComparison.Ordinal));
}
