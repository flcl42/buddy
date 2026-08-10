using System.Globalization;
using Buddy.Core.Domain;

namespace Buddy.App.ViewModels;

public sealed class PronunciationWordViewModel
{
    public PronunciationWordViewModel(PronunciationWord word)
    {
        ArgumentNullException.ThrowIfNull(word);

        Text = word.Text;
        Confidence = word.Confidence;
        ConfidenceText = string.Create(
            CultureInfo.InvariantCulture,
            $"{word.Confidence * 100:0}%");
        TimingText = string.Create(
            CultureInfo.InvariantCulture,
            $"{FormatTimestamp(word.Start)}–{FormatTimestamp(word.End)}");
        AttentionKey = word.Attention.ToString();
        AttentionLabel = word.Attention switch
        {
            PronunciationAttention.LikelyIssue => "Likely unclear",
            PronunciationAttention.Review => "Review",
            _ => "Clear",
        };
        AccessibilityText = string.Create(
            CultureInfo.InvariantCulture,
            $"{Text}, {AttentionLabel}, {ConfidenceText} confidence, {TimingText}");
    }

    public string Text { get; }

    public float Confidence { get; }

    public string ConfidenceText { get; }

    public string TimingText { get; }

    public string AttentionKey { get; }

    public string AttentionLabel { get; }

    public string AccessibilityText { get; }

    private static string FormatTimestamp(TimeSpan timestamp)
    {
        return timestamp.TotalHours >= 1
            ? timestamp.ToString(@"h\:mm\:ss\.f", CultureInfo.InvariantCulture)
            : timestamp.ToString(@"m\:ss\.f", CultureInfo.InvariantCulture);
    }
}
