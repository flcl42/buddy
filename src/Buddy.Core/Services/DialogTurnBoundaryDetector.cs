using Buddy.Core.Abstractions;

namespace Buddy.Core.Services;

public enum DialogTurnDecision
{
    NoSpeech = 0,
    Continue = 1,
    Complete = 2,
}

public sealed record DialogTurnBoundaryEvaluation(
    DialogTurnDecision Decision,
    TimeSpan TrailingSilence,
    TimeSpan RequiredSilence,
    double Progress,
    bool CompletedByMaximumDuration);

public static class DialogTurnBoundaryDetector
{
    public static readonly TimeSpan DefaultAllowedPause =
        TimeSpan.FromMilliseconds(1_100);
    public static readonly TimeSpan MinimumAllowedPause =
        TimeSpan.FromMilliseconds(750);
    public static readonly TimeSpan MaximumAllowedPause =
        TimeSpan.FromSeconds(15);

    private static readonly TimeSpan MaximumUtterance =
        TimeSpan.FromSeconds(45);

    public static DialogTurnDecision Evaluate(
        TimeSpan analyzedDuration,
        IReadOnlyList<DetectedSpeechRegion> speech,
        string? transcript,
        bool force = false)
    {
        return EvaluateDetailed(
            analyzedDuration,
            speech,
            transcript,
            DefaultAllowedPause,
            TimeSpan.Zero,
            force).Decision;
    }

    public static DialogTurnBoundaryEvaluation EvaluateDetailed(
        TimeSpan analyzedDuration,
        IReadOnlyList<DetectedSpeechRegion> speech,
        string? transcript,
        TimeSpan allowedPause,
        TimeSpan countdownResetAt,
        bool force = false)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(
            analyzedDuration,
            TimeSpan.Zero);
        ArgumentNullException.ThrowIfNull(speech);
        ValidateAllowedPause(allowedPause);
        ArgumentOutOfRangeException.ThrowIfLessThan(
            countdownResetAt,
            TimeSpan.Zero);

        string normalized = transcript?.Trim() ?? string.Empty;
        if (speech.Count == 0)
        {
            return new DialogTurnBoundaryEvaluation(
                force && normalized.Length > 0
                    ? DialogTurnDecision.Complete
                    : DialogTurnDecision.NoSpeech,
                TimeSpan.Zero,
                allowedPause,
                force && normalized.Length > 0 ? 1 : 0,
                CompletedByMaximumDuration: false);
        }

        if (normalized.Length == 0)
        {
            return new DialogTurnBoundaryEvaluation(
                DialogTurnDecision.Continue,
                TimeSpan.Zero,
                allowedPause,
                0,
                CompletedByMaximumDuration: false);
        }

        TimeSpan resetAt = countdownResetAt > analyzedDuration
            ? analyzedDuration
            : countdownResetAt;
        TimeSpan lastSpeechEnd = speech
            .Where(region => region.End > region.Start)
            .Select(region => region.End)
            .DefaultIfEmpty(TimeSpan.Zero)
            .Max();
        TimeSpan countdownStart = lastSpeechEnd > resetAt
            ? lastSpeechEnd
            : resetAt;
        TimeSpan trailingSilence = analyzedDuration > countdownStart
            ? analyzedDuration - countdownStart
            : TimeSpan.Zero;
        TimeSpan maximumDurationElapsed = analyzedDuration > resetAt
            ? analyzedDuration - resetAt
            : TimeSpan.Zero;
        bool completedByMaximumDuration =
            maximumDurationElapsed >= MaximumUtterance;
        double progress = Math.Clamp(
            trailingSilence.TotalMilliseconds / allowedPause.TotalMilliseconds,
            0,
            1);

        return new DialogTurnBoundaryEvaluation(
            force
                || completedByMaximumDuration
                || trailingSilence >= allowedPause
                    ? DialogTurnDecision.Complete
                    : DialogTurnDecision.Continue,
            trailingSilence,
            allowedPause,
            force || completedByMaximumDuration ? 1 : progress,
            completedByMaximumDuration);
    }

    public static void ValidateAllowedPause(TimeSpan allowedPause)
    {
        if (allowedPause < MinimumAllowedPause
            || allowedPause > MaximumAllowedPause)
        {
            throw new ArgumentOutOfRangeException(
                nameof(allowedPause),
                allowedPause,
                $"Allowed pause must be between "
                    + $"{MinimumAllowedPause.TotalSeconds:0.##} and "
                    + $"{MaximumAllowedPause.TotalSeconds:0.##} seconds.");
        }
    }
}
