using Buddy.Core.Abstractions;
using Buddy.Core.Domain;
using Buddy.Core.Services;

namespace Buddy.Core.Tests;

public sealed class CompactTimelineBuilderTests
{
    [Fact]
    public void Build_PadsMergesAndCollapsesLongGaps()
    {
        Guid recordingId = Guid.NewGuid();
        DetectedSpeechRegion[] regions =
        [
            new(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), 0.7f),
            new(TimeSpan.FromSeconds(2.2), TimeSpan.FromSeconds(3), 0.9f),
            new(TimeSpan.FromSeconds(7), TimeSpan.FromSeconds(8), 0.8f),
        ];
        CompactTimelineOptions options = new(
            TimeSpan.FromMilliseconds(100),
            TimeSpan.FromMilliseconds(400),
            TimeSpan.FromMilliseconds(200));

        IReadOnlyList<SpeechSegment> result = CompactTimelineBuilder.Build(
            recordingId,
            TimeSpan.FromSeconds(10),
            regions,
            options);

        Assert.Equal(2, result.Count);

        SpeechSegment first = result[0];
        Assert.Equal(TimeSpan.FromSeconds(0.9), first.OriginalStart);
        Assert.Equal(TimeSpan.FromSeconds(3.1), first.OriginalEnd);
        Assert.Equal(TimeSpan.Zero, first.CompactStart);
        Assert.Equal(TimeSpan.FromSeconds(2.2), first.CompactEnd);
        Assert.Equal(0.9f, first.Confidence);

        SpeechSegment second = result[1];
        Assert.Equal(TimeSpan.FromSeconds(6.9), second.OriginalStart);
        Assert.Equal(TimeSpan.FromSeconds(8.1), second.OriginalEnd);
        Assert.Equal(TimeSpan.FromSeconds(2.4), second.CompactStart);
        Assert.Equal(TimeSpan.FromSeconds(3.6), second.CompactEnd);
    }

    [Fact]
    public void Build_ClampsPaddingToSourceBounds()
    {
        DetectedSpeechRegion[] regions =
        [
            new(TimeSpan.FromMilliseconds(20), TimeSpan.FromSeconds(1), 0.8f),
            new(TimeSpan.FromSeconds(4.8), TimeSpan.FromSeconds(5), 0.8f),
        ];

        IReadOnlyList<SpeechSegment> result = CompactTimelineBuilder.Build(
            Guid.NewGuid(),
            TimeSpan.FromSeconds(5),
            regions,
            new CompactTimelineOptions(
                TimeSpan.FromMilliseconds(200),
                TimeSpan.FromMilliseconds(100),
                TimeSpan.FromMilliseconds(200)));

        Assert.Equal(TimeSpan.Zero, result[0].OriginalStart);
        Assert.Equal(TimeSpan.FromSeconds(5), result[1].OriginalEnd);
    }

    [Fact]
    public void Build_ReturnsEmptyForNoSpeech()
    {
        IReadOnlyList<SpeechSegment> result = CompactTimelineBuilder.Build(
            Guid.NewGuid(),
            TimeSpan.FromMinutes(2),
            []);

        Assert.Empty(result);
    }

    [Fact]
    public void Build_RejectsMalformedRegions()
    {
        DetectedSpeechRegion[] regions =
        [
            new(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1), 0.5f),
        ];

        Assert.Throws<ArgumentException>(
            () => CompactTimelineBuilder.Build(Guid.NewGuid(), TimeSpan.FromSeconds(5), regions));
    }
}
