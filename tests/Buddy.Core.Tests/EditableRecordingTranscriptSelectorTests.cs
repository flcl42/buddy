using Buddy.Core.Domain;
using Buddy.Core.Services;

namespace Buddy.Core.Tests;

public sealed class EditableRecordingTranscriptSelectorTests
{
    [Fact]
    public void Select_PrefersLatestRecordingSourceOverPolishedCurrentText()
    {
        Guid recordingId = Guid.NewGuid();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        TranscriptRevision recognized = Create(
            recordingId,
            TranscriptRevisionKind.Recognized,
            "what was spoken",
            now,
            isCurrent: false);
        TranscriptRevision polished = Create(
            recordingId,
            TranscriptRevisionKind.Polished,
            "the improved version",
            now.AddMinutes(1),
            isCurrent: true);

        Assert.Same(
            recognized,
            EditableRecordingTranscriptSelector.Select(
                new[] { recognized, polished }));
    }

    [Fact]
    public void Select_PrefersUserEditOverRecognition()
    {
        Guid recordingId = Guid.NewGuid();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        TranscriptRevision recognized = Create(
            recordingId,
            TranscriptRevisionKind.Recognized,
            "raw",
            now,
            isCurrent: false);
        TranscriptRevision edited = Create(
            recordingId,
            TranscriptRevisionKind.UserEdited,
            "corrected by user",
            now.AddSeconds(1),
            isCurrent: true);

        Assert.Same(
            edited,
            EditableRecordingTranscriptSelector.Select(
                new[] { recognized, edited }));
    }

    [Fact]
    public void Select_KeepsUserEditVisibleAfterLaterRecognitionRetry()
    {
        Guid recordingId = Guid.NewGuid();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        TranscriptRevision edited = Create(
            recordingId,
            TranscriptRevisionKind.UserEdited,
            "corrected by user",
            now,
            isCurrent: true);
        TranscriptRevision retry = Create(
            recordingId,
            TranscriptRevisionKind.Recognized,
            "later automatic retry",
            now.AddSeconds(1),
            isCurrent: false);

        Assert.Same(
            edited,
            EditableRecordingTranscriptSelector.Select(new[] { edited, retry }));
        Assert.False(
            EditableRecordingTranscriptSelector.ShouldPromoteRecognition(edited));
    }

    [Fact]
    public void RecognitionCanBecomeCurrentWhenThereIsNoUserEdit()
    {
        TranscriptRevision recognized = Create(
            Guid.NewGuid(),
            TranscriptRevisionKind.Recognized,
            "first recognition",
            DateTimeOffset.UtcNow,
            isCurrent: true);

        Assert.True(
            EditableRecordingTranscriptSelector.ShouldPromoteRecognition(null));
        Assert.True(
            EditableRecordingTranscriptSelector.ShouldPromoteRecognition(recognized));
    }

    [Fact]
    public void Select_UsesConversationWhenNoRecordingSourceExists()
    {
        TranscriptRevision conversation = Create(
            Guid.NewGuid(),
            TranscriptRevisionKind.Conversation,
            "You: Hello\n\nBuddy: Hi",
            DateTimeOffset.UtcNow,
            isCurrent: true);

        Assert.Same(
            conversation,
            EditableRecordingTranscriptSelector.Select(new[] { conversation }));
    }

    private static TranscriptRevision Create(
        Guid recordingId,
        TranscriptRevisionKind kind,
        string text,
        DateTimeOffset createdAt,
        bool isCurrent) => new(
            Guid.NewGuid(),
            recordingId,
            null,
            kind,
            text,
            new string('0', 64),
            createdAt,
            "test",
            "test-model",
            "test.v1",
            isCurrent);
}
