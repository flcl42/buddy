using Buddy.Core.Services;

namespace Buddy.Core.Tests;

public sealed class DialogTranscriptQualityTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("\"")]
    [InlineData("...D")]
    public void IsUsableRejectsEmptyAndSingleCharacterNoise(string? transcript)
    {
        Assert.False(DialogTranscriptQuality.IsUsable(transcript));
    }

    [Theory]
    [InlineData("No.")]
    [InlineData("I'm fine.")]
    [InlineData("Can you explain that again?")]
    [InlineData("No, no, no.")]
    public void IsUsableAcceptsNormalShortSpeech(string transcript)
    {
        Assert.True(DialogTranscriptQuality.IsUsable(transcript));
    }

    [Fact]
    public void IsUsableRejectsRepeatedDecoderHallucination()
    {
        const string Transcript =
            "\"Heading \"Heading, so it works better. "
            + "\"Heading, so it works better.\"";

        Assert.False(DialogTranscriptQuality.IsUsable(Transcript));
    }

    [Fact]
    public void IsUsableRejectsShortRepeatedBoundaryGarbage()
    {
        Assert.False(
            DialogTranscriptQuality.IsUsable(
                "\"Disk? \"Diskimplow \"Disk\""));
    }
}
