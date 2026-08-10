using System.Diagnostics;
using System.Text;
using Buddy.Core.Abstractions;
using Whisper.net;

namespace Buddy.Speech;

public sealed class WhisperTranscriptionService : ITranscriptionService, IDisposable
{
    private readonly ILocalModelManager _models;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private WhisperFactory? _factory;
    private string? _loadedModelPath;
    private string _backend = "CPU";
    private bool _disposed;

    public WhisperTranscriptionService(ILocalModelManager models)
    {
        _models = models ?? throw new ArgumentNullException(nameof(models));
    }

    public async Task<TranscriptionResult> TranscribeAsync(
        string audioPath,
        TranscriptionOptions options,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(audioPath);
        ArgumentNullException.ThrowIfNull(options);
        string fullAudioPath = Path.GetFullPath(audioPath);
        if (!File.Exists(fullAudioPath))
        {
            throw new FileNotFoundException(
                "The audio prepared for transcription is missing.",
                fullAudioPath);
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string modelPath = await GetReadyModelPathAsync(cancellationToken)
                .ConfigureAwait(false);
            WhisperFactory factory = GetOrCreateFactory(modelPath);
            WhisperProcessorBuilder builder = factory.CreateBuilder()
                .WithThreads(Math.Max(1, Environment.ProcessorCount / 2))
                .WithProbabilities()
                .WithProgressHandler(
                    percent => progress?.Report(Math.Clamp(percent / 100d, 0, 1)));

            if (string.Equals(options.Language, "auto", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(options.Language))
            {
                builder.WithLanguageDetection();
            }
            else
            {
                builder.WithLanguage(options.Language.Trim().ToLowerInvariant());
            }

            if (!string.IsNullOrWhiteSpace(options.InitialPrompt))
            {
                builder.WithPrompt(options.InitialPrompt.Trim());
            }

            if (options.IncludeWordTimestamps)
            {
                builder.WithTokenTimestamps().SplitOnWord();
            }

            await using WhisperProcessor processor = builder.Build();
            await using FileStream input = new(
                fullAudioPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            Stopwatch stopwatch = Stopwatch.StartNew();
            List<TranscriptionSegment> segments = [];
            List<TranscriptionToken> tokens = [];
            StringBuilder text = new();
            string detectedLanguage = string.Empty;

            await foreach (SegmentData segment in processor
                .ProcessAsync(input, cancellationToken)
                .ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string segmentText = segment.Text;
                if (!string.IsNullOrWhiteSpace(segmentText))
                {
                    text.Append(segmentText);
                    segments.Add(new TranscriptionSegment(
                        segment.Start,
                        segment.End,
                        segmentText.Trim(),
                        segment.Probability));
                }

                if (options.IncludeWordTimestamps)
                {
                    foreach (WhisperToken token in segment.Tokens)
                    {
                        long startUnits = Math.Max(0, token.Start);
                        long endUnits = Math.Max(startUnits, token.End);
                        tokens.Add(new TranscriptionToken(
                            token.Text ?? string.Empty,
                            TimeSpan.FromMilliseconds(startUnits * 10d),
                            TimeSpan.FromMilliseconds(endUnits * 10d),
                            token.Probability));
                    }
                }

                if (string.IsNullOrWhiteSpace(detectedLanguage)
                    && !string.IsNullOrWhiteSpace(segment.Language))
                {
                    detectedLanguage = segment.Language;
                }
            }

            stopwatch.Stop();
            progress?.Report(1);
            if (string.IsNullOrWhiteSpace(detectedLanguage))
            {
                detectedLanguage = string.Equals(
                        options.Language,
                        "auto",
                        StringComparison.OrdinalIgnoreCase)
                    ? "unknown"
                    : options.Language;
            }

            return new TranscriptionResult(
                text.ToString().Trim(),
                detectedLanguage,
                segments,
                tokens,
                $"{LocalSpeechModels.WhisperLargeV3Turbo} ({_backend})",
                stopwatch.Elapsed);
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
        _factory?.Dispose();
        _gate.Dispose();
        GC.SuppressFinalize(this);
    }

    private WhisperFactory GetOrCreateFactory(string modelPath)
    {
        if (_factory is not null
            && string.Equals(
                _loadedModelPath,
                modelPath,
                StringComparison.OrdinalIgnoreCase))
        {
            return _factory;
        }

        _factory?.Dispose();
        _factory = null;
        _loadedModelPath = null;

        try
        {
            _factory = WhisperFactory.FromPath(
                modelPath,
                new WhisperFactoryOptions
                {
                    UseGpu = true,
                    UseFlashAttention = true,
                    GpuDevice = 0,
                });
            _backend = "CUDA";
        }
        catch (Exception error) when (CanRetryOnCpu(error))
        {
            _factory = WhisperFactory.FromPath(
                modelPath,
                new WhisperFactoryOptions
                {
                    UseGpu = false,
                    UseFlashAttention = false,
                });
            _backend = "CPU fallback";
        }

        _loadedModelPath = modelPath;
        return _factory;
    }

    private async Task<string> GetReadyModelPathAsync(
        CancellationToken cancellationToken)
    {
        IReadOnlyList<LocalModelInfo> models = await _models
            .GetModelsAsync(cancellationToken)
            .ConfigureAwait(false);
        LocalModelInfo? model = models.FirstOrDefault(
            item => item.Id == LocalSpeechModels.WhisperLargeV3Turbo);
        if (model?.Status != LocalModelStatus.Ready
            || string.IsNullOrWhiteSpace(model.LocalPath))
        {
            throw new LocalModelNotInstalledException(
                LocalSpeechModels.WhisperLargeV3Turbo,
                model?.DisplayName ?? "Whisper large-v3-turbo");
        }

        return model.LocalPath;
    }

    private static bool CanRetryOnCpu(Exception error)
    {
        return error is WhisperModelLoadException
            or DllNotFoundException
            or TypeInitializationException
            or BadImageFormatException
            or InvalidOperationException;
    }
}
