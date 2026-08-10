using Buddy.Core.Abstractions;
using Buddy.Core.Services;

namespace Buddy.Core.Tests;

public sealed class DialogTurnBoundaryDetectorTests
{
    [Fact]
    public void EvaluateReturnsNoSpeechForSilentAudio()
    {
        DialogTurnDecision decision = DialogTurnBoundaryDetector.Evaluate(
            TimeSpan.FromSeconds(2),
            [],
            string.Empty);

        Assert.Equal(DialogTurnDecision.NoSpeech, decision);
    }

    [Fact]
    public void EvaluateKeepsListeningWhileTrailingPauseIsShort()
    {
        DetectedSpeechRegion[] speech =
        [
            new(
                TimeSpan.FromMilliseconds(100),
                TimeSpan.FromSeconds(1),
                0.9f),
        ];

        DialogTurnDecision decision = DialogTurnBoundaryDetector.Evaluate(
            TimeSpan.FromMilliseconds(1_500),
            speech,
            "Tell me about this");

        Assert.Equal(DialogTurnDecision.Continue, decision);
    }

    [Fact]
    public void EvaluateCompletesAfterNormalTrailingSilence()
    {
        DetectedSpeechRegion[] speech =
        [
            new(
                TimeSpan.FromMilliseconds(100),
                TimeSpan.FromSeconds(1),
                0.9f),
        ];

        DialogTurnDecision decision = DialogTurnBoundaryDetector.Evaluate(
            TimeSpan.FromMilliseconds(2_150),
            speech,
            "Tell me about this");

        Assert.Equal(DialogTurnDecision.Complete, decision);
    }

    [Fact]
    public void EvaluateUsesTheConfiguredPauseEvenWithTerminalPunctuation()
    {
        DetectedSpeechRegion[] speech =
        [
            new(
                TimeSpan.FromMilliseconds(100),
                TimeSpan.FromSeconds(1),
                0.9f),
        ];

        DialogTurnDecision decision = DialogTurnBoundaryDetector.Evaluate(
            TimeSpan.FromMilliseconds(1_700),
            speech,
            "Can you explain this?");

        Assert.Equal(DialogTurnDecision.Continue, decision);
    }

    [Fact]
    public void DetailedEvaluationReportsConfiguredSilenceProgress()
    {
        DetectedSpeechRegion[] speech =
        [
            new(
                TimeSpan.FromMilliseconds(100),
                TimeSpan.FromSeconds(1),
                0.9f),
        ];

        DialogTurnBoundaryEvaluation evaluation =
            DialogTurnBoundaryDetector.EvaluateDetailed(
                TimeSpan.FromMilliseconds(2_500),
                speech,
                "I am still forming this idea",
                TimeSpan.FromSeconds(2),
                TimeSpan.Zero);

        Assert.Equal(DialogTurnDecision.Continue, evaluation.Decision);
        Assert.Equal(TimeSpan.FromMilliseconds(1_500), evaluation.TrailingSilence);
        Assert.Equal(TimeSpan.FromSeconds(2), evaluation.RequiredSilence);
        Assert.Equal(0.75, evaluation.Progress, precision: 3);
        Assert.False(evaluation.CompletedByMaximumDuration);
    }

    [Fact]
    public void CountdownResetPostponesCompletionAndCanBeRepeated()
    {
        DetectedSpeechRegion[] speech =
        [
            new(TimeSpan.Zero, TimeSpan.FromSeconds(1), 0.9f),
        ];
        TimeSpan allowedPause = TimeSpan.FromSeconds(2);

        DialogTurnBoundaryEvaluation beforeReset =
            DialogTurnBoundaryDetector.EvaluateDetailed(
                TimeSpan.FromSeconds(3),
                speech,
                "This is a longer idea",
                allowedPause,
                TimeSpan.Zero);
        DialogTurnBoundaryEvaluation afterReset =
            DialogTurnBoundaryDetector.EvaluateDetailed(
                TimeSpan.FromSeconds(3),
                speech,
                "This is a longer idea",
                allowedPause,
                TimeSpan.FromSeconds(2.8));
        DialogTurnBoundaryEvaluation afterSecondReset =
            DialogTurnBoundaryDetector.EvaluateDetailed(
                TimeSpan.FromSeconds(5),
                speech,
                "This is a longer idea",
                allowedPause,
                TimeSpan.FromSeconds(4.8));

        Assert.Equal(DialogTurnDecision.Complete, beforeReset.Decision);
        Assert.Equal(DialogTurnDecision.Continue, afterReset.Decision);
        Assert.Equal(0.1, afterReset.Progress, precision: 3);
        Assert.Equal(DialogTurnDecision.Continue, afterSecondReset.Decision);
        Assert.Equal(0.1, afterSecondReset.Progress, precision: 3);
    }

    [Fact]
    public void ResetAlsoExtendsTheMaximumUtteranceWindow()
    {
        DetectedSpeechRegion[] speech =
        [
            new(TimeSpan.Zero, TimeSpan.FromSeconds(49), 0.9f),
        ];

        DialogTurnBoundaryEvaluation evaluation =
            DialogTurnBoundaryDetector.EvaluateDetailed(
                TimeSpan.FromSeconds(50),
                speech,
                "A deliberately long thought",
                TimeSpan.FromSeconds(3),
                TimeSpan.FromSeconds(20));

        Assert.Equal(DialogTurnDecision.Continue, evaluation.Decision);
        Assert.False(evaluation.CompletedByMaximumDuration);
    }

    [Fact]
    public void DetailedEvaluationRejectsAnUnsafePauseSetting()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => DialogTurnBoundaryDetector.EvaluateDetailed(
                TimeSpan.FromSeconds(1),
                [],
                string.Empty,
                TimeSpan.FromMilliseconds(100),
                TimeSpan.Zero));
    }

    [Fact]
    public void EvaluateCompletesForcedAndMaximumLengthTurns()
    {
        DetectedSpeechRegion[] shortSpeech =
        [
            new(
                TimeSpan.Zero,
                TimeSpan.FromMilliseconds(900),
                0.9f),
        ];
        DetectedSpeechRegion[] longSpeech =
        [
            new(
                TimeSpan.Zero,
                TimeSpan.FromSeconds(44.9),
                0.9f),
        ];

        Assert.Equal(
            DialogTurnDecision.Complete,
            DialogTurnBoundaryDetector.Evaluate(
                TimeSpan.FromSeconds(1),
                shortSpeech,
                "Send this now",
                force: true));
        Assert.Equal(
            DialogTurnDecision.Complete,
            DialogTurnBoundaryDetector.Evaluate(
                TimeSpan.FromSeconds(45),
                longSpeech,
                "This turn reached its maximum duration"));
    }
}
