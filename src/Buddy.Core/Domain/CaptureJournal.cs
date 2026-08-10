using Buddy.Core.Abstractions;

namespace Buddy.Core.Domain;

public enum CaptureJournalState
{
    Capturing = 0,
    Stopping = 1,
    Interrupted = 2,
    Finalized = 3,
}

public sealed record CaptureJournal(
    Guid SessionId,
    Guid RecordingId,
    RecordingKind Kind,
    CaptureJournalState State,
    DateTimeOffset StartedAt,
    DateTimeOffset UpdatedAt,
    string? InputDeviceId,
    int SampleRate,
    int BitsPerSample,
    int Channels,
    AudioSampleEncoding Encoding,
    int NextChunkIndex,
    long TotalPcmBytes);
