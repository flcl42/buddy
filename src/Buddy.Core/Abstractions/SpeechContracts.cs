using Buddy.Core.Domain;

namespace Buddy.Core.Abstractions;

public sealed record SpeechDetectionOptions(
    float Threshold = 0.5f,
    TimeSpan? MinimumSpeech = null,
    TimeSpan? MinimumSilence = null,
    TimeSpan? Padding = null);

public sealed record DetectedSpeechRegion(
    TimeSpan Start,
    TimeSpan End,
    float Confidence);

public interface IVoiceActivityService
{
    Task<IReadOnlyList<DetectedSpeechRegion>> DetectAsync(
        string audioPath,
        SpeechDetectionOptions options,
        CancellationToken cancellationToken = default);
}

public sealed record TranscriptionOptions(
    string Language = "auto",
    string? InitialPrompt = null,
    bool IncludeWordTimestamps = false);

public sealed record TranscriptionSegment(
    TimeSpan Start,
    TimeSpan End,
    string Text,
    float? Confidence);

public sealed record TranscriptionToken(
    string Text,
    TimeSpan Start,
    TimeSpan End,
    float Confidence);

public sealed record TranscriptionResult(
    string Text,
    string DetectedLanguage,
    IReadOnlyList<TranscriptionSegment> Segments,
    IReadOnlyList<TranscriptionToken> Tokens,
    string Model,
    TimeSpan ProcessingTime);

public interface ITranscriptionService
{
    Task<TranscriptionResult> TranscribeAsync(
        string audioPath,
        TranscriptionOptions options,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);
}

public interface IPhoneticTranscriptionService
{
    Task<string> TranscribeAsync(
        string text,
        string locale = "en-US",
        CancellationToken cancellationToken = default);
}

public sealed record SpeechVoice(
    string Id,
    string DisplayName,
    string Locale,
    string GenderLabel);

public sealed record SpeechSynthesisOptions(
    string VoiceId,
    float Speed = 1.0f,
    IReadOnlyList<GlossaryTerm>? Glossary = null);

public sealed record SpeechSynthesisResult(
    string OutputPath,
    TimeSpan Duration,
    string Model,
    string VoiceId);

public interface ISpeechSynthesisService
{
    Task<IReadOnlyList<SpeechVoice>> GetVoicesAsync(
        CancellationToken cancellationToken = default);

    Task<SpeechSynthesisResult> SynthesizeAsync(
        string text,
        string outputPath,
        SpeechSynthesisOptions options,
        CancellationToken cancellationToken = default);
}

public enum LocalModelStatus
{
    NotInstalled = 0,
    Downloading = 1,
    Ready = 2,
    Invalid = 3,
}

public sealed record LocalModelInfo(
    string Id,
    string DisplayName,
    long DownloadBytes,
    string Sha256,
    LocalModelStatus Status,
    string? LocalPath);

public interface ILocalModelManager
{
    Task<IReadOnlyList<LocalModelInfo>> GetModelsAsync(
        CancellationToken cancellationToken = default);

    Task EnsureInstalledAsync(
        string modelId,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);
}
