using Buddy.Core.Domain;

namespace Buddy.Core.Services;

/// <summary>
/// Keeps every consumer on the same user-facing recording derivative.
/// The untouched original remains the recovery source; the compact artifact is
/// canonical for listening, seeking, waveforms, transcription, and export.
/// </summary>
public static class CanonicalAudioArtifactSelector
{
    public static AudioArtifact? Select(
        IEnumerable<AudioArtifact> artifacts)
    {
        ArgumentNullException.ThrowIfNull(artifacts);

        AudioArtifact? original = null;
        foreach (AudioArtifact artifact in artifacts)
        {
            if (artifact.Kind == AudioArtifactKind.Compact)
            {
                return artifact;
            }

            if (artifact.Kind == AudioArtifactKind.Original)
            {
                original ??= artifact;
            }
        }

        return original;
    }

    public static AudioArtifact? SelectOriginal(
        IEnumerable<AudioArtifact> artifacts)
    {
        ArgumentNullException.ThrowIfNull(artifacts);
        return artifacts.FirstOrDefault(
            artifact => artifact.Kind == AudioArtifactKind.Original);
    }
}
