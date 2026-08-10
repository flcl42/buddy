using Buddy.Core.Abstractions;
using Buddy.Core.Domain;
using Buddy.Core.Services;

namespace Buddy.Core.Tests;

public sealed class PronunciationAssessmentBuilderTests
{
    [Fact]
    public void BuildCombinesSubwordTokensAndAttachesPunctuation()
    {
        Guid recordingId = Guid.NewGuid();
        TranscriptionToken[] tokens =
        [
            Token("[_BEG_]", 0, 0, 0.4f),
            Token(" Let", 10, 20, 0.9f),
            Token(" me", 20, 30, 0.8f),
            Token(" Alex", 30, 45, 0.9f),
            Token("ey", 45, 50, 0.6f),
            Token(".", 50, 50, 0.7f),
            Token("[_TT_25]", 50, 50, 0.1f),
        ];

        PronunciationAssessment? result = PronunciationAssessmentBuilder.Build(
            recordingId,
            "Let me Alexey.",
            "Whisper",
            DateTimeOffset.UtcNow,
            tokens);

        Assert.NotNull(result);
        Assert.Equal(["Let", "me", "Alexey."], result.Words.Select(word => word.Text));
        Assert.Equal(TimeSpan.FromMilliseconds(30), result.Words[2].Start);
        Assert.Equal(TimeSpan.FromMilliseconds(50), result.Words[2].End);
        Assert.Equal(0.8f, result.Words[2].Confidence, precision: 3);
    }

    [Theory]
    [InlineData(0.9f, PronunciationAttention.Clear)]
    [InlineData(0.75f, PronunciationAttention.Clear)]
    [InlineData(0.74f, PronunciationAttention.Review)]
    [InlineData(0.55f, PronunciationAttention.Review)]
    [InlineData(0.54f, PronunciationAttention.LikelyIssue)]
    public void ClassifyUsesStableAttentionThresholds(
        float confidence,
        PronunciationAttention expected)
    {
        Assert.Equal(expected, PronunciationScoring.Classify(confidence));
    }

    [Fact]
    public void AssessmentCalculatesPaceAndReviewCounts()
    {
        Guid recordingId = Guid.NewGuid();
        PronunciationAssessment assessment = new(
            recordingId,
            "One two three.",
            "wʌn tuː θriː",
            DateTimeOffset.UtcNow,
            "Whisper",
            PronunciationAssessmentBuilder.SchemaVersion,
            [
                new(
                    recordingId,
                    0,
                    "One",
                    TimeSpan.Zero,
                    TimeSpan.FromMilliseconds(400),
                    0.9f),
                new(
                    recordingId,
                    1,
                    "two",
                    TimeSpan.FromMilliseconds(500),
                    TimeSpan.FromMilliseconds(900),
                    0.7f),
                new(
                    recordingId,
                    2,
                    "three.",
                    TimeSpan.FromSeconds(1),
                    TimeSpan.FromSeconds(1.5),
                    0.4f),
            ]);

        Assert.Equal(120, assessment.WordsPerMinute, precision: 3);
        Assert.Equal(1, assessment.ReviewWordCount);
        Assert.Equal(1, assessment.LikelyIssueWordCount);
        Assert.Equal(2f / 3f, assessment.OverallConfidence, precision: 3);
    }

    private static TranscriptionToken Token(
        string text,
        int startMilliseconds,
        int endMilliseconds,
        float confidence)
    {
        return new TranscriptionToken(
            text,
            TimeSpan.FromMilliseconds(startMilliseconds),
            TimeSpan.FromMilliseconds(endMilliseconds),
            confidence);
    }
}
