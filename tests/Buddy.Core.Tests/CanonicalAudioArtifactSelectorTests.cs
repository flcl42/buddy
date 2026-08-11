using Buddy.Core.Domain;
using Buddy.Core.Services;

namespace Buddy.Core.Tests;

public sealed class CanonicalAudioArtifactSelectorTests
{
    [Fact]
    public void Select_PrefersCompactRegardlessOfStorageOrder()
    {
        Guid recordingId = Guid.NewGuid();
        AudioArtifact original = Create(
            recordingId,
            AudioArtifactKind.Original,
            "original.opus",
            TimeSpan.FromMinutes(8));
        AudioArtifact compact = Create(
            recordingId,
            AudioArtifactKind.Compact,
            "compact.opus",
            TimeSpan.FromMinutes(5));

        AudioArtifact? selected = CanonicalAudioArtifactSelector.Select(
            new[] { original, compact });

        Assert.Same(compact, selected);
    }

    [Fact]
    public void Select_FallsBackToUntouchedOriginal()
    {
        AudioArtifact original = Create(
            Guid.NewGuid(),
            AudioArtifactKind.Original,
            "original.opus",
            TimeSpan.FromMinutes(2));

        Assert.Same(
            original,
            CanonicalAudioArtifactSelector.Select(new[] { original }));
    }

    [Fact]
    public void Select_IgnoresGeneratedSpeechArtifacts()
    {
        AudioArtifact generated = Create(
            Guid.NewGuid(),
            AudioArtifactKind.DialogAssistant,
            "answer.wav",
            TimeSpan.FromSeconds(8));

        Assert.Null(CanonicalAudioArtifactSelector.Select(new[] { generated }));
    }

    private static AudioArtifact Create(
        Guid recordingId,
        AudioArtifactKind kind,
        string path,
        TimeSpan duration) => new(
            Guid.NewGuid(),
            recordingId,
            kind,
            path,
            AudioContainer.OggOpus,
            48_000,
            1,
            duration,
            1_024,
            new string('0', 64),
            null,
            DateTimeOffset.UtcNow);
}
