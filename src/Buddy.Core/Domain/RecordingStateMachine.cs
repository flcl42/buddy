namespace Buddy.Core.Domain;

public static class RecordingStateMachine
{
    private static readonly Dictionary<RecordingStatus, HashSet<RecordingStatus>> AllowedTransitions =
        new()
        {
            [RecordingStatus.Capturing] = Set(
                RecordingStatus.FinalizingSource,
                RecordingStatus.Interrupted,
                RecordingStatus.NeedsAttention,
                RecordingStatus.Deleted),
            [RecordingStatus.FinalizingSource] = Set(
                RecordingStatus.ReadyForPlayback,
                RecordingStatus.NeedsAttention),
            [RecordingStatus.ReadyForPlayback] = Set(
                RecordingStatus.DetectingSpeech,
                RecordingStatus.Transcribing,
                RecordingStatus.Ready,
                RecordingStatus.NeedsAttention,
                RecordingStatus.Deleted),
            [RecordingStatus.DetectingSpeech] = Set(
                RecordingStatus.BuildingCompactAudio,
                RecordingStatus.Transcribing,
                RecordingStatus.NeedsAttention),
            [RecordingStatus.BuildingCompactAudio] = Set(
                RecordingStatus.Transcribing,
                RecordingStatus.Ready,
                RecordingStatus.NeedsAttention),
            [RecordingStatus.Transcribing] = Set(
                RecordingStatus.Titling,
                RecordingStatus.Ready,
                RecordingStatus.NeedsAttention),
            [RecordingStatus.Titling] = Set(
                RecordingStatus.Ready,
                RecordingStatus.NeedsAttention),
            [RecordingStatus.Ready] = Set(
                RecordingStatus.DetectingSpeech,
                RecordingStatus.Transcribing,
                RecordingStatus.Titling,
                RecordingStatus.NeedsAttention,
                RecordingStatus.Deleted),
            [RecordingStatus.NeedsAttention] = Set(
                RecordingStatus.FinalizingSource,
                RecordingStatus.DetectingSpeech,
                RecordingStatus.BuildingCompactAudio,
                RecordingStatus.Transcribing,
                RecordingStatus.Titling,
                RecordingStatus.Ready,
                RecordingStatus.Deleted),
            [RecordingStatus.Interrupted] = Set(
                RecordingStatus.Recovering,
                RecordingStatus.NeedsAttention,
                RecordingStatus.Deleted),
            [RecordingStatus.Recovering] = Set(
                RecordingStatus.FinalizingSource,
                RecordingStatus.NeedsAttention),
            [RecordingStatus.Deleted] = new HashSet<RecordingStatus>(),
        };

    public static bool CanTransition(RecordingStatus current, RecordingStatus next)
    {
        return AllowedTransitions.TryGetValue(current, out HashSet<RecordingStatus>? allowed)
            && allowed.Contains(next);
    }

    public static void EnsureTransition(RecordingStatus current, RecordingStatus next)
    {
        if (!CanTransition(current, next))
        {
            throw new InvalidOperationException($"Recording cannot transition from {current} to {next}.");
        }
    }

    private static HashSet<RecordingStatus> Set(params RecordingStatus[] values)
    {
        return new HashSet<RecordingStatus>(values);
    }
}
