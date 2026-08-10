using Buddy.Core.Abstractions;
using Whisper.net;

namespace Buddy.Speech;

public sealed class WhisperVoiceActivityService : IVoiceActivityService, IDisposable
{
    private static readonly TimeSpan DefaultMinimumSpeech =
        TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan DefaultMinimumSilence =
        TimeSpan.FromMilliseconds(350);

    private readonly ILocalModelManager _models;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    public WhisperVoiceActivityService(ILocalModelManager models)
    {
        _models = models ?? throw new ArgumentNullException(nameof(models));
    }

    public async Task<IReadOnlyList<DetectedSpeechRegion>> DetectAsync(
        string audioPath,
        SpeechDetectionOptions options,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(audioPath);
        ArgumentNullException.ThrowIfNull(options);
        if (options.Threshold is <= 0 or >= 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.Threshold,
                "Speech detection threshold must be between zero and one.");
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _models.EnsureInstalledAsync(
                    LocalSpeechModels.SileroVad,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            string modelPath = await GetReadyModelPathAsync(
                    LocalSpeechModels.SileroVad,
                    cancellationToken)
                .ConfigureAwait(false);

            using WhisperVadFactory factory = WhisperVadFactory.FromPath(
                modelPath,
                new WhisperFactoryOptions
                {
                    UseGpu = false,
                });
            WhisperVadProcessorBuilder builder = factory.CreateBuilder()
                .WithUseGpu(false)
                .WithThreads(Math.Max(1, Environment.ProcessorCount / 2))
                .WithThreshold(options.Threshold)
                .WithMinSpeechDuration(options.MinimumSpeech ?? DefaultMinimumSpeech)
                .WithMinSilenceDuration(options.MinimumSilence ?? DefaultMinimumSilence)
                .WithMaxSpeechDuration(TimeSpan.FromSeconds(30))
                .WithSpeechPadding(options.Padding ?? TimeSpan.Zero)
                .WithSamplesOverlap(TimeSpan.FromMilliseconds(100));
            await using WhisperVadProcessor processor = builder.Build();
            await using FileStream input = new(
                Path.GetFullPath(audioPath),
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            IReadOnlyList<VadSegmentData> detected = await processor
                .DetectSpeechAsync(input, cancellationToken)
                .ConfigureAwait(false);
            return detected
                .Where(segment => segment.End > segment.Start)
                .Select(
                    segment => new DetectedSpeechRegion(
                        segment.Start,
                        segment.End,
                        options.Threshold))
                .ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _gate.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<string> GetReadyModelPathAsync(
        string modelId,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<LocalModelInfo> models = await _models
            .GetModelsAsync(cancellationToken)
            .ConfigureAwait(false);
        LocalModelInfo? model = models.FirstOrDefault(item => item.Id == modelId);
        if (model?.Status != LocalModelStatus.Ready
            || string.IsNullOrWhiteSpace(model.LocalPath))
        {
            throw new LocalModelNotInstalledException(
                modelId,
                model?.DisplayName ?? modelId);
        }

        return model.LocalPath;
    }
}
