using System.Net;
using System.Text;
using System.Text.Json;
using Buddy.Core.Abstractions;
using Buddy.Core.Domain;

namespace Buddy.Language.Tests;

public sealed class QwenLanguageProviderTests
{
    [Fact]
    public async Task ImproveUsesAuthenticatedLocalEndpointWithoutDeepSeekThinkingField()
    {
        string providerPayload = JsonSerializer.Serialize(
            new
            {
                schema_version = "buddy.improvement.v1",
                corrected = "We are testing the local model.",
                polished = "We're testing the local model.",
                changes = Array.Empty<object>(),
                ambiguities = Array.Empty<object>(),
                protected_term_violations = Array.Empty<string>(),
            });
        CaptureHandler handler = new(CreateChatResponse(providerPayload));
        using HttpClient client = new(handler);
        FakeQwenRuntime runtime = new();
        QwenLanguageProvider provider = new(
            client,
            new EmptySecretStore(),
            runtime,
            new ReadyQwenInstaller());

        ImprovementResult result = await provider.ImproveAsync(
            new ImprovementRequest(
                "We testing local model.",
                ImprovementMode.Natural,
                "en-US",
                [],
                null));

        Assert.Equal(QwenLanguageProvider.ProviderIdValue, result.Provider);
        Assert.Equal(QwenLanguageProvider.ModelAlias, result.Model);
        Assert.Equal(1, runtime.LoadCalls);
        Assert.Equal(runtime.ApiKey, handler.AuthorizationToken);
        using JsonDocument sent = JsonDocument.Parse(Assert.IsType<string>(handler.LastBody));
        Assert.False(sent.RootElement.TryGetProperty("thinking", out _));
        Assert.Equal(20, sent.RootElement.GetProperty("top_k").GetInt32());
        Assert.Equal(0.8, sent.RootElement.GetProperty("top_p").GetDouble());
        Assert.Equal(1.5, sent.RootElement.GetProperty("presence_penalty").GetDouble());
    }

    [Fact]
    public async Task DialogRejectsOversizedCompleteHistoryInsteadOfTruncatingIt()
    {
        CaptureHandler handler = new();
        using HttpClient client = new(handler);
        FakeQwenRuntime runtime = new();
        QwenLanguageProvider provider = new(
            client,
            new EmptySecretStore(),
            runtime,
            new ReadyQwenInstaller());
        string longTurn = new('a', QwenLanguageProvider.MaximumConversationCharacters);

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.RespondAsync(
                new ConversationRequest(
                    "Keep every turn.",
                    [new ConversationTurn(DialogMessageRole.User, longTurn)],
                    "en-US")));

        Assert.Contains("complete history", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(handler.LastBody);
    }

    [Fact]
    public async Task RouterDefaultsToBuddyProxyAndPersistsAnExplicitDeepSeekChoice()
    {
        CaptureHandler handler = new();
        using HttpClient client = new(handler);
        FakeQwenRuntime runtime = new();
        MemorySettingsStore settings = new();
        LanguageProviderRouter router = new(
            new BuddyProxyLanguageProvider(
                client,
                new EmptySecretStore(),
                new Uri("https://proxy.example/v1/"),
                "ABCDEF-GHIJKL"),
            new QwenLanguageProvider(
                client,
                new EmptySecretStore(),
                runtime,
                new ReadyQwenInstaller()),
            new DeepSeekLanguageProvider(client, new EmptySecretStore()),
            settings);

        await router.LoadAsync();

        Assert.Equal(BuddyProxyLanguageProvider.ProviderIdValue, router.ProviderId);
        Assert.Null(settings.Value);

        await router.SelectAsync(DeepSeekLanguageProvider.ProviderIdValue);

        Assert.Equal(DeepSeekLanguageProvider.ProviderIdValue, router.ProviderId);
        Assert.Equal(DeepSeekLanguageProvider.ProviderIdValue, settings.Value);
    }

    [Fact]
    public void RuntimeDefaultsReserveGpuMemoryAndKeepDialogTurnsWarm()
    {
        QwenRuntimeOptions options = new(
            Path.Combine(Path.GetTempPath(), "buddy-qwen-runtime"),
            Path.Combine(Path.GetTempPath(), "buddy-qwen-model.gguf"),
            Path.Combine(Path.GetTempPath(), "buddy-qwen-logs"));

        Assert.Equal(24, options.GpuLayers);
        Assert.Equal(0, options.DraftGpuLayers);
        Assert.Equal(3, options.SpeculativeDraftTokens);
        Assert.Equal(32_768, options.ContextSize);
        Assert.Equal(120, options.SleepIdleSeconds);
        Assert.Equal(17_845, options.Port);
    }

    private static HttpResponseMessage CreateChatResponse(string providerPayload)
    {
        string response = JsonSerializer.Serialize(
            new
            {
                model = QwenLanguageProvider.ModelAlias,
                choices = new[]
                {
                    new
                    {
                        message = new
                        {
                            content = providerPayload,
                        },
                        finish_reason = "stop",
                    },
                },
                usage = new
                {
                    prompt_tokens = 31,
                    completion_tokens = 17,
                },
            });
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(response, Encoding.UTF8, "application/json"),
        };
    }

    private sealed class CaptureHandler(params HttpResponseMessage[] responses)
        : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new(responses);

        public string? LastBody { get; private set; }

        public string? AuthorizationToken { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            AuthorizationToken = request.Headers.Authorization?.Parameter;
            LastBody = request.Content is null
                ? null
                : await request.Content
                    .ReadAsStringAsync(cancellationToken)
                    .ConfigureAwait(false);
            HttpResponseMessage response = _responses.Dequeue();
            response.RequestMessage = request;
            return response;
        }
    }

    private sealed class FakeQwenRuntime : IQwenModelRuntime
    {
        public FakeQwenRuntime()
        {
            string root = Path.Combine(Path.GetTempPath(), "buddy-qwen-tests");
            Options = new QwenRuntimeOptions(
                    root,
                    Path.Combine(root, "model.gguf"),
                    Path.Combine(root, "logs"))
                .Validate();
        }

        public QwenRuntimeOptions Options { get; }

        public string ApiKey { get; } = "local-test-key";

        public int LoadCalls { get; private set; }

        public Task<QwenRuntimeStatus> GetStatusAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(
                new QwenRuntimeStatus(
                    QwenRuntimeState.ReadyOnDisk,
                    "Ready",
                    DateTimeOffset.Now));
        }

        public Task EnsureLoadedAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LoadCalls++;
            return Task.CompletedTask;
        }

        public Task<int> CountTokensAsync(
            string content,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(content.Length);
        }

        public Task UnloadAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
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

    private sealed class MemorySettingsStore : IAppSettingsStore
    {
        public string? Value { get; private set; }

        public Task<string?> GetAsync(
            string key,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Value);
        }

        public Task SetAsync(
            string key,
            string value,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Value = value;
            return Task.CompletedTask;
        }

        public Task RemoveAsync(
            string key,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Value = null;
            return Task.CompletedTask;
        }
    }
}
