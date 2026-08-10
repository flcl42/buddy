using Buddy.Core.Abstractions;
using Buddy.Core.Domain;

namespace Buddy.Language.Tests;

public sealed class LiveQwenLanguageProviderTests
{
    [Fact]
    [Trait("Category", "Live")]
    public async Task OfficialLocalModelSatisfiesEveryBuddyLanguageContract()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("BUDDY_RUN_LIVE_QWEN_TESTS"),
                "1",
                StringComparison.Ordinal))
        {
            return;
        }

        using CancellationTokenSource timeout = new(TimeSpan.FromMinutes(10));
        using HttpClient client = new()
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
        await using QwenModelRuntime runtime = new(
            client,
            new QwenRuntimeOptions(
                @"D:\ai\Buddy\llama.cpp\b10243",
                @"D:\ai\Buddy\models\Qwen3.6-27B-Q4_K_M.gguf",
                @"D:\ai\Buddy\logs",
                DraftModelPath:
                    @"D:\ai\Buddy\models\dflash-Qwen3.6-27B-Q8_0.gguf"));
        QwenLanguageProvider provider = new(
            client,
            new EmptySecretStore(),
            runtime,
            new ReadyQwenInstaller());

        ImprovementResult improvement = await provider.ImproveAsync(
            new ImprovementRequest(
                "Yesterday I go to meeting and explain our new local model.",
                ImprovementMode.Natural,
                "en-US",
                [new GlossaryTerm("Buddy", null, true)],
                "professional"),
            timeout.Token);
        Assert.False(string.IsNullOrWhiteSpace(improvement.Corrected));
        Assert.Equal(QwenLanguageProvider.ProviderIdValue, improvement.Provider);

        TitleResult title = await provider.CreateTitleAsync(
            new TitleRequest(
                "We discussed adding a private local language model to Buddy.",
                RecordingKind.Meeting,
                "en-US"),
            timeout.Token);
        Assert.InRange(title.Title.Length, 1, 160);

        ConversationResult conversation = await provider.RespondAsync(
            new ConversationRequest(
                "Answer clearly and remember the whole session.",
                [
                    new ConversationTurn(
                        DialogMessageRole.User,
                        "My project is called Buddy."),
                    new ConversationTurn(
                        DialogMessageRole.Assistant,
                        "Understood."),
                    new ConversationTurn(
                        DialogMessageRole.User,
                        "What is my project called, and why might local inference help privacy?"),
                ],
                "en-US"),
            timeout.Token);
        Assert.Contains("Buddy", conversation.Answer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Buddy", conversation.SpokenAnswer, StringComparison.OrdinalIgnoreCase);

        WordDefinitionResult definition = await provider.DefineAsync(
            new WordDefinitionRequest(
                "inference",
                "Local inference can keep private dialog text on this computer.",
                "en-US"),
            timeout.Token);
        Assert.False(string.IsNullOrWhiteSpace(definition.Definition));
        Assert.Equal(QwenLanguageProvider.ProviderIdValue, definition.Provider);
    }

    private sealed class EmptySecretStore : ISecretStore
    {
        public Task<string?> GetAsync(
            string key,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<string?>(null);
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

    private sealed class ReadyQwenInstaller : IQwenModelInstaller
    {
        public Task<QwenInstallStatus> GetStatusAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(
                new QwenInstallStatus(
                    QwenInstallState.Ready,
                    "Ready",
                    DateTimeOffset.Now));
        }

        public Task EnsureInstalledAsync(
            IProgress<QwenInstallProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new QwenInstallProgress(1, "Ready", 1, 1));
            return Task.CompletedTask;
        }
    }
}
