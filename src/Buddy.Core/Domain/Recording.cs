namespace Buddy.Core.Domain;

public enum RecordingKind
{
    Meeting = 0,
    Trainer = 1,
    Dialog = 2,
}

public enum RecordingStatus
{
    Capturing = 0,
    FinalizingSource = 1,
    ReadyForPlayback = 2,
    DetectingSpeech = 3,
    BuildingCompactAudio = 4,
    Transcribing = 5,
    Titling = 6,
    Ready = 7,
    NeedsAttention = 8,
    Interrupted = 9,
    Recovering = 10,
    Deleted = 11,
}

public sealed record Recording(
    Guid Id,
    RecordingKind Kind,
    DateTimeOffset CreatedAt,
    DateTimeOffset CaptureStartedAt,
    DateTimeOffset? CaptureEndedAt,
    TimeSpan WallDuration,
    TimeSpan SpeechDuration,
    string? InputDeviceId,
    RecordingStatus Status,
    string DisplayTitle,
    string? GeneratedTitle,
    string? LastErrorCode,
    string? LastErrorMessage,
    DateTimeOffset? DeletedAt,
    long Version)
{
    public static Recording Start(
        RecordingKind kind,
        DateTimeOffset startedAt,
        string? inputDeviceId = null,
        Guid? id = null)
    {
        Guid recordingId = id ?? Guid.NewGuid();

        return new Recording(
            recordingId,
            kind,
            startedAt,
            startedAt,
            null,
            TimeSpan.Zero,
            TimeSpan.Zero,
            inputDeviceId,
            RecordingStatus.Capturing,
            RecordingTitles.CreateFallback(kind, startedAt),
            null,
            null,
            null,
            null,
            0);
    }

    public Recording CompleteCapture(DateTimeOffset endedAt)
    {
        if (endedAt < CaptureStartedAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(endedAt),
                endedAt,
                "Capture end cannot precede capture start.");
        }

        Recording transitioned = TransitionTo(RecordingStatus.FinalizingSource);
        return transitioned with
        {
            CaptureEndedAt = endedAt,
            WallDuration = endedAt - CaptureStartedAt,
        };
    }

    public Recording TransitionTo(
        RecordingStatus next,
        string? errorCode = null,
        string? errorMessage = null)
    {
        RecordingStateMachine.EnsureTransition(Status, next);

        return this with
        {
            Status = next,
            LastErrorCode = next == RecordingStatus.NeedsAttention ? errorCode : null,
            LastErrorMessage = next == RecordingStatus.NeedsAttention ? errorMessage : null,
            Version = checked(Version + 1),
        };
    }

    public Recording WithDurations(TimeSpan wallDuration, TimeSpan speechDuration)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(wallDuration, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(speechDuration, TimeSpan.Zero);

        if (speechDuration > wallDuration)
        {
            throw new ArgumentOutOfRangeException(
                nameof(speechDuration),
                speechDuration,
                "Speech duration cannot exceed wall duration.");
        }

        return this with
        {
            WallDuration = wallDuration,
            SpeechDuration = speechDuration,
            Version = checked(Version + 1),
        };
    }

    public Recording WithGeneratedTitle(string generatedTitle)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(generatedTitle);
        string normalized = generatedTitle.Trim();

        return this with
        {
            DisplayTitle = normalized,
            GeneratedTitle = normalized,
            Version = checked(Version + 1),
        };
    }

    public Recording Rename(string displayTitle)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayTitle);

        return this with
        {
            DisplayTitle = displayTitle.Trim(),
            Version = checked(Version + 1),
        };
    }

    public Recording SoftDelete(DateTimeOffset deletedAt)
    {
        Recording transitioned = TransitionTo(RecordingStatus.Deleted);
        return transitioned with { DeletedAt = deletedAt };
    }
}

public sealed record RecordingQuery(
    string? Search = null,
    RecordingKind? Kind = null,
    RecordingStatus? Status = null,
    bool IncludeDeleted = false,
    int Limit = 100,
    int Offset = 0)
{
    public RecordingQuery Validate()
    {
        if (Limit is < 1 or > 500)
        {
            throw new ArgumentOutOfRangeException(nameof(Limit), Limit, "Limit must be between 1 and 500.");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(Offset);
        return this;
    }
}
