using Buddy.Core.Domain;

namespace Buddy.Core.Abstractions;

public interface ILanguageImprovementProvider
{
    string ProviderId { get; }

    Task<ImprovementResult> ImproveAsync(
        ImprovementRequest request,
        CancellationToken cancellationToken = default);

    Task<TitleResult> CreateTitleAsync(
        TitleRequest request,
        CancellationToken cancellationToken = default);

    Task<ProviderHealth> CheckHealthAsync(CancellationToken cancellationToken = default);
}

public interface IConversationProvider
{
    string ProviderId { get; }

    Task<ConversationResult> RespondAsync(
        ConversationRequest request,
        CancellationToken cancellationToken = default);
}

public interface IWordDefinitionProvider
{
    string ProviderId { get; }

    Task<WordDefinitionResult> DefineAsync(
        WordDefinitionRequest request,
        CancellationToken cancellationToken = default);
}

public enum ProviderHealthStatus
{
    NotConfigured = 0,
    Available = 1,
    Unauthorized = 2,
    QuotaUnavailable = 3,
    Offline = 4,
    Error = 5,
}

public sealed record ProviderHealth(
    ProviderHealthStatus Status,
    string Message,
    DateTimeOffset CheckedAt);

public interface ISecretStore
{
    Task<string?> GetAsync(string key, CancellationToken cancellationToken = default);

    Task SetAsync(string key, string value, CancellationToken cancellationToken = default);

    Task RemoveAsync(string key, CancellationToken cancellationToken = default);
}
