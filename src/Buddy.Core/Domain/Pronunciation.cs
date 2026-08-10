namespace Buddy.Core.Domain;

public enum PronunciationAttention
{
    Clear = 0,
    Review = 1,
    LikelyIssue = 2,
}

public sealed record PronunciationWord(
    Guid SourceId,
    int Sequence,
    string Text,
    TimeSpan Start,
    TimeSpan End,
    float Confidence)
{
    public TimeSpan Duration => End - Start;

    public PronunciationAttention Attention =>
        PronunciationScoring.Classify(Confidence);
}

public sealed record PronunciationAssessment(
    Guid RecordingId,
    string Transcript,
    string PhoneticTranscript,
    DateTimeOffset CreatedAt,
    string Model,
    string SchemaVersion,
    IReadOnlyList<PronunciationWord> Words)
{
    public float OverallConfidence => Words.Count == 0
        ? 0
        : Words.Average(word => word.Confidence);

    public int ReviewWordCount => Words.Count(
        word => word.Attention == PronunciationAttention.Review);

    public int LikelyIssueWordCount => Words.Count(
        word => word.Attention == PronunciationAttention.LikelyIssue);

    public double WordsPerMinute
    {
        get
        {
            if (Words.Count == 0)
            {
                return 0;
            }

            TimeSpan speakingSpan = Words[^1].End - Words[0].Start;
            return speakingSpan.TotalSeconds <= 0
                ? 0
                : Words.Count * 60d / speakingSpan.TotalSeconds;
        }
    }
}

public sealed record DialogPronunciationAssessment(
    Guid MessageId,
    string Transcript,
    string PhoneticTranscript,
    DateTimeOffset CreatedAt,
    string Model,
    string SchemaVersion,
    IReadOnlyList<PronunciationWord> Words)
{
    public float OverallConfidence => Words.Count == 0
        ? 0
        : Words.Average(word => word.Confidence);

    public int ReviewWordCount => Words.Count(
        word => word.Attention == PronunciationAttention.Review);

    public int LikelyIssueWordCount => Words.Count(
        word => word.Attention == PronunciationAttention.LikelyIssue);
}

public static class PronunciationScoring
{
    public const float LikelyIssueThreshold = 0.55f;
    public const float ReviewThreshold = 0.75f;

    public static PronunciationAttention Classify(float confidence)
    {
        return confidence switch
        {
            < LikelyIssueThreshold => PronunciationAttention.LikelyIssue,
            < ReviewThreshold => PronunciationAttention.Review,
            _ => PronunciationAttention.Clear,
        };
    }
}
