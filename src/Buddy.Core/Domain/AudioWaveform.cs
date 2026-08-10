namespace Buddy.Core.Domain;

public sealed record AudioWaveform(
    Guid ArtifactId,
    TimeSpan Duration,
    IReadOnlyList<byte> Peaks,
    DateTimeOffset CreatedAt,
    string SchemaVersion)
{
    public const int DefaultSampleCount = 96;

    public IReadOnlyList<float> Normalize()
    {
        return Peaks.Select(peak => peak / 255f).ToArray();
    }
}
