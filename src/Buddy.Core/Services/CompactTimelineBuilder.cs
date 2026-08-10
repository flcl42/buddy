using Buddy.Core.Abstractions;
using Buddy.Core.Domain;

namespace Buddy.Core.Services;

public sealed record CompactTimelineOptions(
    TimeSpan Padding,
    TimeSpan MaximumNaturalPause,
    TimeSpan CollapsedPause)
{
    public static CompactTimelineOptions Default { get; } = new(
        TimeSpan.FromMilliseconds(180),
        TimeSpan.FromMilliseconds(450),
        TimeSpan.FromMilliseconds(200));
}

public static class CompactTimelineBuilder
{
    public static IReadOnlyList<SpeechSegment> Build(
        Guid recordingId,
        TimeSpan sourceDuration,
        IReadOnlyList<DetectedSpeechRegion> detectedRegions,
        CompactTimelineOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(detectedRegions);
        ArgumentOutOfRangeException.ThrowIfLessThan(sourceDuration, TimeSpan.Zero);

        CompactTimelineOptions effective = options ?? CompactTimelineOptions.Default;
        ValidateOptions(effective);

        if (detectedRegions.Count == 0)
        {
            return [];
        }

        List<DetectedSpeechRegion> normalized = detectedRegions
            .Select(region => PadAndClamp(region, sourceDuration, effective.Padding))
            .Where(region => region.End > region.Start)
            .OrderBy(region => region.Start)
            .ToList();

        List<DetectedSpeechRegion> merged = MergeNearby(normalized, effective.MaximumNaturalPause);
        List<SpeechSegment> segments = new(merged.Count);
        TimeSpan compactCursor = TimeSpan.Zero;

        for (int index = 0; index < merged.Count; index++)
        {
            DetectedSpeechRegion region = merged[index];
            TimeSpan compactStart = compactCursor;
            TimeSpan compactEnd = compactStart + (region.End - region.Start);

            segments.Add(new SpeechSegment(
                recordingId,
                index,
                region.Start,
                region.End,
                compactStart,
                compactEnd,
                region.Confidence));

            compactCursor = compactEnd;
            if (index < merged.Count - 1)
            {
                compactCursor += effective.CollapsedPause;
            }
        }

        return segments;
    }

    private static DetectedSpeechRegion PadAndClamp(
        DetectedSpeechRegion region,
        TimeSpan sourceDuration,
        TimeSpan padding)
    {
        if (region.Start < TimeSpan.Zero || region.End < region.Start)
        {
            throw new ArgumentException("Detected speech regions must have ordered, non-negative times.", nameof(region));
        }

        TimeSpan start = region.Start > padding ? region.Start - padding : TimeSpan.Zero;
        TimeSpan end = region.End + padding;
        if (end > sourceDuration)
        {
            end = sourceDuration;
        }

        return region with { Start = start, End = end };
    }

    private static List<DetectedSpeechRegion> MergeNearby(
        IReadOnlyList<DetectedSpeechRegion> regions,
        TimeSpan maximumNaturalPause)
    {
        List<DetectedSpeechRegion> merged = [];

        foreach (DetectedSpeechRegion region in regions)
        {
            if (merged.Count == 0)
            {
                merged.Add(region);
                continue;
            }

            DetectedSpeechRegion previous = merged[^1];
            TimeSpan gap = region.Start - previous.End;
            if (gap <= maximumNaturalPause)
            {
                merged[^1] = previous with
                {
                    End = region.End > previous.End ? region.End : previous.End,
                    Confidence = Math.Max(previous.Confidence, region.Confidence),
                };
            }
            else
            {
                merged.Add(region);
            }
        }

        return merged;
    }

    private static void ValidateOptions(CompactTimelineOptions options)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(options.Padding, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaximumNaturalPause, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.CollapsedPause, TimeSpan.Zero);
    }
}
