using Buddy.Core.Domain;

namespace Buddy.Core.Tests;

public sealed class RecordingTests
{
    [Fact]
    public void Start_CreatesCapturingRecordingWithDeterministicFallbackTitle()
    {
        DateTimeOffset startedAt = new(2026, 7, 30, 14, 5, 0, TimeSpan.FromHours(3));
        Guid id = Guid.Parse("21ab1bb2-d221-44e8-b33e-f111f54231b0");

        Recording recording = Recording.Start(
            RecordingKind.Meeting,
            startedAt,
            "microphone-1",
            id);

        Assert.Equal(id, recording.Id);
        Assert.Equal(RecordingStatus.Capturing, recording.Status);
        Assert.Equal("Meeting · 30 Jul · 14:05", recording.DisplayTitle);
        Assert.Equal("microphone-1", recording.InputDeviceId);
        Assert.Null(recording.CaptureEndedAt);
        Assert.Equal(0, recording.Version);
    }

    [Fact]
    public void CompleteCapture_SetsDurationAndTransitions()
    {
        DateTimeOffset startedAt = new(2026, 7, 30, 14, 5, 0, TimeSpan.Zero);
        Recording recording = Recording.Start(RecordingKind.Trainer, startedAt);

        Recording completed = recording.CompleteCapture(startedAt.AddSeconds(9));

        Assert.Equal(RecordingStatus.FinalizingSource, completed.Status);
        Assert.Equal(TimeSpan.FromSeconds(9), completed.WallDuration);
        Assert.Equal(startedAt.AddSeconds(9), completed.CaptureEndedAt);
        Assert.Equal(1, completed.Version);
    }

    [Fact]
    public void CompleteCapture_RejectsEndBeforeStart()
    {
        DateTimeOffset startedAt = new(2026, 7, 30, 14, 5, 0, TimeSpan.Zero);
        Recording recording = Recording.Start(RecordingKind.Trainer, startedAt);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => recording.CompleteCapture(startedAt.AddMilliseconds(-1)));
    }

    [Fact]
    public void TransitionTo_RejectsSkippedDurabilityStage()
    {
        Recording recording = Recording.Start(RecordingKind.Meeting, DateTimeOffset.UtcNow);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => recording.TransitionTo(RecordingStatus.Transcribing));

        Assert.Contains("Capturing", error.Message, StringComparison.Ordinal);
        Assert.Contains("Transcribing", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TransitionTo_AllowsExpectedProcessingPath()
    {
        DateTimeOffset startedAt = DateTimeOffset.UtcNow;
        Recording recording = Recording.Start(RecordingKind.Meeting, startedAt)
            .CompleteCapture(startedAt.AddMinutes(2))
            .TransitionTo(RecordingStatus.ReadyForPlayback)
            .TransitionTo(RecordingStatus.DetectingSpeech)
            .TransitionTo(RecordingStatus.BuildingCompactAudio)
            .TransitionTo(RecordingStatus.Transcribing)
            .TransitionTo(RecordingStatus.Titling)
            .TransitionTo(RecordingStatus.Ready);

        Assert.Equal(RecordingStatus.Ready, recording.Status);
        Assert.Equal(7, recording.Version);
    }

    [Fact]
    public void WithDurations_RejectsSpeechLongerThanSource()
    {
        Recording recording = Recording.Start(RecordingKind.Meeting, DateTimeOffset.UtcNow);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => recording.WithDurations(TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void WithGeneratedTitle_PreservesGeneratedTitleWhenUserRenamesDisplayTitle()
    {
        Recording recording = Recording.Start(RecordingKind.Meeting, DateTimeOffset.UtcNow)
            .WithGeneratedTitle("Devnet rollout review")
            .Rename("Friday protocol meeting");

        Assert.Equal("Friday protocol meeting", recording.DisplayTitle);
        Assert.Equal("Devnet rollout review", recording.GeneratedTitle);
    }
}
