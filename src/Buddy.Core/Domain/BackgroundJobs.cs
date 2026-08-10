namespace Buddy.Core.Domain;

public enum BackgroundJobType
{
    FinalizeSource = 0,
    DetectSpeech = 1,
    BuildCompactAudio = 2,
    Transcribe = 3,
    GenerateTitle = 4,
    GenerateSpeech = 5,
    AnalyzePronunciation = 6,
    BuildWaveform = 7,
}

public enum BackgroundJobState
{
    Pending = 0,
    Running = 1,
    Completed = 2,
    Failed = 3,
}

public sealed record BackgroundJob(
    Guid Id,
    Guid? RecordingId,
    BackgroundJobType Type,
    string PayloadJson,
    BackgroundJobState State,
    int AttemptCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset AvailableAt,
    DateTimeOffset? LeaseExpiresAt,
    string? LeaseOwner,
    string? LastErrorCode,
    string? LastErrorMessage);
