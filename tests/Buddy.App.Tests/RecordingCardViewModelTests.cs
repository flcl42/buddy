using Buddy.App.ViewModels;
using Buddy.Core.Domain;

namespace Buddy.App.Tests;

public sealed class RecordingCardViewModelTests
{
    [Fact]
    public void Constructor_UsesCompactDurationAsHeadline()
    {
        Guid recordingId = Guid.NewGuid();
        Recording recording = ReadyRecording(
            recordingId,
            wallDuration: TimeSpan.FromMinutes(8),
            speechDuration: TimeSpan.FromMinutes(4));
        AudioArtifact compact = new(
            Guid.NewGuid(),
            recordingId,
            AudioArtifactKind.Compact,
            "compact.opus",
            AudioContainer.OggOpus,
            48_000,
            1,
            TimeSpan.FromMinutes(5),
            1_024,
            new string('0', 64),
            "Buddy compact timeline",
            DateTimeOffset.UtcNow);

        RecordingCardViewModel card = new(recording, compact);

        Assert.Equal("5:00", card.DurationText);
        Assert.Equal("8:00 captured · 4:00 speech", card.SpeechDurationText);
        Assert.Equal(TimeSpan.FromMinutes(5), card.PlaybackDuration);
    }

    [Fact]
    public void TranscriptDraft_SurvivesRefreshOnlyForSameSourceRevision()
    {
        Recording recording = ReadyRecording(
            Guid.NewGuid(),
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(20));
        TranscriptRevision source = Transcript(recording.Id, "original words");
        RecordingCardViewModel first = new(
            recording,
            sourceTranscript: source)
        {
            IsTranscriptExpanded = true,
            TranscriptText = "corrected words",
        };

        RecordingTranscriptUiState state = first.CaptureTranscriptUiState();
        RecordingCardViewModel refreshed = new(
            recording,
            sourceTranscript: source);
        refreshed.RestoreTranscriptUiState(state);

        Assert.True(refreshed.IsTranscriptExpanded);
        Assert.Equal("corrected words", refreshed.TranscriptText);
        Assert.True(refreshed.IsTranscriptDirty);
    }

    private static Recording ReadyRecording(
        Guid id,
        TimeSpan wallDuration,
        TimeSpan speechDuration)
    {
        DateTimeOffset started = DateTimeOffset.UtcNow - wallDuration;
        return new Recording(
            id,
            RecordingKind.Meeting,
            started,
            started,
            started + wallDuration,
            wallDuration,
            speechDuration,
            "default",
            RecordingStatus.Ready,
            "Recording",
            null,
            null,
            null,
            null,
            4);
    }

    private static TranscriptRevision Transcript(Guid recordingId, string text) =>
        new(
            Guid.NewGuid(),
            recordingId,
            null,
            TranscriptRevisionKind.Recognized,
            text,
            new string('0', 64),
            DateTimeOffset.UtcNow,
            "Whisper.net",
            "large-v3-turbo",
            "buddy.transcript.v1",
            true);
}
