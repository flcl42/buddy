using Buddy.App.ViewModels;
using Buddy.Core.Domain;

namespace Buddy.App.Tests;

public sealed class PronunciationWordViewModelTests
{
    [Theory]
    [InlineData(0.91f, "Clear", "91%")]
    [InlineData(0.68f, "Review", "68%")]
    [InlineData(0.43f, "LikelyIssue", "43%")]
    public void ExposesStableHighlightAndConfidenceText(
        float confidence,
        string attentionKey,
        string confidenceText)
    {
        PronunciationWordViewModel viewModel = new(new PronunciationWord(
            Guid.NewGuid(),
            0,
            "example",
            TimeSpan.FromMilliseconds(100),
            TimeSpan.FromMilliseconds(650),
            confidence));

        Assert.Equal(attentionKey, viewModel.AttentionKey);
        Assert.Equal(confidenceText, viewModel.ConfidenceText);
        Assert.Equal("0:00.1–0:00.6", viewModel.TimingText);
        Assert.Contains("confidence", viewModel.AccessibilityText);
    }
}
