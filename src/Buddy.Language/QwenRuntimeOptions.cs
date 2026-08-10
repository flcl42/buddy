namespace Buddy.Language;

public sealed record QwenRuntimeOptions(
    string RuntimeDirectory,
    string ModelPath,
    string LogDirectory,
    string? DraftModelPath = null,
    int Port = 17_845,
    int ContextSize = 32_768,
    int GpuLayers = 24,
    int DraftGpuLayers = 0,
    int SpeculativeDraftTokens = 3,
    int CpuThreads = 16,
    int SleepIdleSeconds = 120,
    TimeSpan? StartupTimeout = null)
{
    public const string ServerFileName = "llama-server.exe";

    public string ServerPath => Path.Combine(RuntimeDirectory, ServerFileName);

    public Uri BaseAddress => new($"http://127.0.0.1:{Port}/");

    public TimeSpan EffectiveStartupTimeout =>
        StartupTimeout ?? TimeSpan.FromMinutes(5);

    public bool IsDFlashEnabled => !string.IsNullOrWhiteSpace(DraftModelPath);

    public QwenRuntimeOptions Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(RuntimeDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(ModelPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(LogDirectory);
        if (Port is < 1_024 or > 65_535)
        {
            throw new ArgumentOutOfRangeException(nameof(Port));
        }

        if (ContextSize is < 8_192 or > 262_144)
        {
            throw new ArgumentOutOfRangeException(nameof(ContextSize));
        }

        if (GpuLayers < 0
            || DraftGpuLayers < 0
            || SpeculativeDraftTokens is < 1 or > 16
            || CpuThreads < 1
            || SleepIdleSeconds < -1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(GpuLayers),
                "GPU layers must be non-negative, draft tokens must be between 1 and 16, and CPU threads must be positive.");
        }

        if (EffectiveStartupTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(StartupTimeout));
        }

        return this with
        {
            RuntimeDirectory = Path.GetFullPath(RuntimeDirectory),
            ModelPath = Path.GetFullPath(ModelPath),
            LogDirectory = Path.GetFullPath(LogDirectory),
            DraftModelPath = string.IsNullOrWhiteSpace(DraftModelPath)
                ? null
                : Path.GetFullPath(DraftModelPath),
        };
    }
}
