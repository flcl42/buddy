using Buddy.Core.Abstractions;
using Buddy.Core.Domain;

namespace Buddy.Language;

public sealed class QwenLanguageProvider :
    ILanguageImprovementProvider,
    IConversationProvider,
    IWordDefinitionProvider
{
    public const string ProviderIdValue = "qwen-local";
    public const string ModelAlias = "Qwen3.6-27B-Q4_K_M";
    public const int MaximumConversationCharacters = 70_000;
    private const int ChatTemplateTokenReserve = 1_200;

    private readonly IQwenModelRuntime _runtime;
    private readonly IQwenModelInstaller _installer;
    private readonly StructuredLanguageProvider _inner;

    public QwenLanguageProvider(
        HttpClient httpClient,
        ISecretStore secrets,
        IQwenModelRuntime runtime,
        IQwenModelInstaller installer)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(secrets);
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _installer = installer ?? throw new ArgumentNullException(nameof(installer));
        Uri baseAddress = _runtime.Options.BaseAddress;
        _inner = new StructuredLanguageProvider(
            httpClient,
            secrets,
            new StructuredLanguageProviderOptions(
                ProviderIdValue,
                "Qwen 3.6 27B",
                ModelAlias,
                new Uri(baseAddress, "v1/chat/completions"),
                new Uri(baseAddress, "v1/models"),
                TimeSpan.FromMinutes(15),
                MaximumConversationCharacters,
                StaticApiKey: _runtime.ApiKey,
                TopK: 20,
                TopP: 0.8,
                PresencePenalty: 1.5)
                .Validate());
    }

    public string ProviderId => ProviderIdValue;

    public async Task<ImprovementResult> ImproveAsync(
        ImprovementRequest request,
        CancellationToken cancellationToken = default)
    {
        await _installer.EnsureInstalledAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        await _runtime.EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
        return await _inner.ImproveAsync(request, cancellationToken).ConfigureAwait(false);
    }

    public async Task<TitleResult> CreateTitleAsync(
        TitleRequest request,
        CancellationToken cancellationToken = default)
    {
        await _installer.EnsureInstalledAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        await _runtime.EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
        return await _inner.CreateTitleAsync(request, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ConversationResult> RespondAsync(
        ConversationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SystemInstruction);
        ArgumentNullException.ThrowIfNull(request.Messages);
        await _installer.EnsureInstalledAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        await _runtime.EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
        string context = request.SystemInstruction
            + Environment.NewLine
            + string.Join(
                Environment.NewLine,
                request.Messages.Select(turn => $"{turn.Role}: {turn.Content}"));
        int promptTokens = await _runtime
            .CountTokensAsync(context, cancellationToken)
            .ConfigureAwait(false);
        int maximumCompletionTokens = Math.Min(
            8_000,
            Math.Max(800, request.MaximumOutputCharacters * 2 / 3));
        if (promptTokens + maximumCompletionTokens + ChatTemplateTokenReserve
            > _runtime.Options.ContextSize)
        {
            throw new InvalidOperationException(
                "This dialog is too large for Qwen's local context. Buddy kept the complete "
                + "history; finish this recording and start a new dialog rather than silently "
                + "losing older context.");
        }

        return await _inner.RespondAsync(request, cancellationToken).ConfigureAwait(false);
    }

    public async Task<WordDefinitionResult> DefineAsync(
        WordDefinitionRequest request,
        CancellationToken cancellationToken = default)
    {
        await _installer.EnsureInstalledAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        await _runtime.EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
        return await _inner.DefineAsync(request, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ProviderHealth> CheckHealthAsync(
        CancellationToken cancellationToken = default)
    {
        QwenInstallStatus installation = await _installer
            .GetStatusAsync(cancellationToken)
            .ConfigureAwait(false);
        if (installation.State != QwenInstallState.Ready)
        {
            return new ProviderHealth(
                ProviderHealthStatus.NotConfigured,
                installation.Message,
                installation.CheckedAt);
        }

        QwenRuntimeStatus status = await _runtime
            .GetStatusAsync(cancellationToken)
            .ConfigureAwait(false);
        return new ProviderHealth(
            status.State switch
            {
                QwenRuntimeState.Missing => ProviderHealthStatus.NotConfigured,
                QwenRuntimeState.Failed => ProviderHealthStatus.Error,
                _ => ProviderHealthStatus.Available,
            },
            status.Message,
            status.CheckedAt);
    }
}
