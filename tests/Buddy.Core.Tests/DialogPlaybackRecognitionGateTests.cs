using Buddy.Core.Services;

namespace Buddy.Core.Tests;

public sealed class DialogPlaybackRecognitionGateTests
{
    private static readonly DateTimeOffset Start =
        new(2026, 7, 30, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void InactiveGateAcceptsCaptureChunks()
    {
        DialogPlaybackRecognitionGate gate = new();

        DialogPlaybackChunkDecision decision = gate.Evaluate(
            Start,
            isPlaybackActive: false);

        Assert.Equal(DialogPlaybackChunkDecision.Accept, decision);
    }

    [Fact]
    public void ActivePlaybackAndFeedbackTailAreDiscarded()
    {
        DialogPlaybackRecognitionGate gate = new(
            TimeSpan.FromMilliseconds(350));
        gate.Begin(Start);

        Assert.Equal(
            DialogPlaybackChunkDecision.Discard,
            gate.Evaluate(Start.AddSeconds(5), isPlaybackActive: true));

        gate.PlaybackStopped(Start.AddSeconds(5));

        Assert.Equal(
            DialogPlaybackChunkDecision.Discard,
            gate.Evaluate(
                Start.AddMilliseconds(5_349),
                isPlaybackActive: false));
    }

    [Fact]
    public void FirstCleanBoundaryIsDiscardedBeforeCaptureResumes()
    {
        DialogPlaybackRecognitionGate gate = new(
            TimeSpan.FromMilliseconds(350));
        gate.Begin(Start);
        gate.PlaybackStopped(Start.AddSeconds(5));

        Assert.Equal(
            DialogPlaybackChunkDecision.DiscardAndResume,
            gate.Evaluate(
                Start.AddMilliseconds(5_350),
                isPlaybackActive: false));
        Assert.Equal(
            DialogPlaybackChunkDecision.Accept,
            gate.Evaluate(Start.AddSeconds(6), isPlaybackActive: false));
    }

    [Fact]
    public void ResetImmediatelyRestoresCapture()
    {
        DialogPlaybackRecognitionGate gate = new();
        gate.Begin(Start);

        gate.Reset();

        Assert.Equal(
            DialogPlaybackChunkDecision.Accept,
            gate.Evaluate(Start, isPlaybackActive: true));
    }
}
