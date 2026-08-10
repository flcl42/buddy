namespace Buddy.Core.Domain;

public enum AudioArtifactKind
{
    Original = 0,
    Compact = 1,
    TrainerGenerated = 2,
    DialogAssistant = 3,
}

public enum AudioContainer
{
    Wave = 0,
    OggOpus = 1,
}

public sealed record AudioArtifact(
    Guid Id,
    Guid RecordingId,
    AudioArtifactKind Kind,
    string RelativePath,
    AudioContainer Container,
    int SampleRate,
    int Channels,
    TimeSpan Duration,
    long ByteLength,
    string Sha256,
    string? Generator,
    DateTimeOffset CreatedAt);

public sealed record SpeechSegment(
    Guid RecordingId,
    int Sequence,
    TimeSpan OriginalStart,
    TimeSpan OriginalEnd,
    TimeSpan CompactStart,
    TimeSpan CompactEnd,
    float Confidence)
{
    public TimeSpan OriginalDuration => OriginalEnd - OriginalStart;

    public TimeSpan CompactDuration => CompactEnd - CompactStart;
}

public enum TranscriptRevisionKind
{
    Recognized = 0,
    UserEdited = 1,
    Corrected = 2,
    Polished = 3,
    Conversation = 4,
}

public sealed record TranscriptRevision(
    Guid Id,
    Guid RecordingId,
    Guid? ParentRevisionId,
    TranscriptRevisionKind Kind,
    string Text,
    string ContentSha256,
    DateTimeOffset CreatedAt,
    string? Provider,
    string? Model,
    string? SchemaVersion,
    bool IsCurrent);
