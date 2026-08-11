using Buddy.Core.Domain;

namespace Buddy.Core.Services;

public static class EditableRecordingTranscriptSelector
{
    public static TranscriptRevision? Select(
        IEnumerable<TranscriptRevision> revisions)
    {
        ArgumentNullException.ThrowIfNull(revisions);
        TranscriptRevision[] ordered = revisions
            .OrderBy(revision => revision.CreatedAt)
            .ToArray();

        return ordered.LastOrDefault(
                revision => revision.Kind == TranscriptRevisionKind.UserEdited)
            ?? ordered.LastOrDefault(
                revision => revision.Kind == TranscriptRevisionKind.Recognized)
            ?? ordered.LastOrDefault(revision => revision.IsCurrent)
            ?? ordered.LastOrDefault();
    }

    public static bool ShouldPromoteRecognition(TranscriptRevision? current) =>
        current?.Kind != TranscriptRevisionKind.UserEdited;
}
