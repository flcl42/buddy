using Buddy.Language;

namespace Buddy.App.Services;

public sealed class UnsupportedQwenModelRuntime : IQwenModelRuntime
{
    private const string Message =
        "Local Qwen is currently available in the Windows build. "
        + "Use Buddy Trial or DeepSeek on this desktop preview.";

    public UnsupportedQwenModelRuntime(QwenRuntimeOptions options)
    {
        Options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public QwenRuntimeOptions Options { get; }

    public string ApiKey => string.Empty;

    public Task<QwenRuntimeStatus> GetStatusAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(
            new QwenRuntimeStatus(
                QwenRuntimeState.Missing,
                Message,
                DateTimeOffset.Now));
    }

    public Task EnsureLoadedAsync(CancellationToken cancellationToken = default) =>
        Task.FromException(new PlatformNotSupportedException(Message));

    public Task<int> CountTokensAsync(
        string content,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Math.Max(1, (content?.Length ?? 0) / 4));
    }

    public Task UnloadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public sealed class UnsupportedQwenModelInstaller : IQwenModelInstaller
{
    private const string Message =
        "Local Qwen is currently available in the Windows build. "
        + "Use Buddy Trial or DeepSeek on this desktop preview.";

    public Task<QwenInstallStatus> GetStatusAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(
            new QwenInstallStatus(
                QwenInstallState.Missing,
                Message,
                DateTimeOffset.Now));
    }

    public Task EnsureInstalledAsync(
        IProgress<QwenInstallProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        Task.FromException(new PlatformNotSupportedException(Message));
}
