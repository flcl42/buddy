namespace Buddy.Core.Services;

public enum DialogPlaybackChunkDecision
{
    Accept = 0,
    Discard = 1,
    DiscardAndResume = 2,
}

public sealed class DialogPlaybackRecognitionGate
{
    public static readonly TimeSpan DefaultFeedbackGuard =
        TimeSpan.FromMilliseconds(350);

    private readonly TimeSpan _feedbackGuard;
    private DateTimeOffset _suppressUntil;

    public DialogPlaybackRecognitionGate()
        : this(DefaultFeedbackGuard)
    {
    }

    public DialogPlaybackRecognitionGate(TimeSpan feedbackGuard)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(
            feedbackGuard,
            TimeSpan.Zero);
        _feedbackGuard = feedbackGuard;
    }

    public bool IsActive { get; private set; }

    public void Begin(DateTimeOffset now)
    {
        IsActive = true;
        _suppressUntil = now.Add(_feedbackGuard);
    }

    public void PlaybackStopped(DateTimeOffset now)
    {
        if (IsActive)
        {
            _suppressUntil = now.Add(_feedbackGuard);
        }
    }

    public DialogPlaybackChunkDecision Evaluate(
        DateTimeOffset completedAt,
        bool isPlaybackActive)
    {
        if (!IsActive)
        {
            return DialogPlaybackChunkDecision.Accept;
        }

        if (isPlaybackActive || completedAt < _suppressUntil)
        {
            return DialogPlaybackChunkDecision.Discard;
        }

        IsActive = false;
        _suppressUntil = DateTimeOffset.MinValue;
        return DialogPlaybackChunkDecision.DiscardAndResume;
    }

    public void Reset()
    {
        IsActive = false;
        _suppressUntil = DateTimeOffset.MinValue;
    }
}
