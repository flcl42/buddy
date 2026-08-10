using Buddy.Core.Domain;

namespace Buddy.Core.Tests;

public sealed class DialogTests
{
    [Fact]
    public void SessionLifecyclePreservesIdentityAndUsesVersions()
    {
        Guid recordingId = Guid.NewGuid();
        Guid sessionId = Guid.NewGuid();
        DateTimeOffset startedAt =
            new(2026, 7, 30, 10, 0, 0, TimeSpan.FromHours(3));
        DialogSession active = DialogSession.Start(
            recordingId,
            startedAt,
            "Use all prior turns.",
            sessionId);

        DialogSession completing = active.BeginCompletion();
        DialogSession completed = completing.Complete(startedAt.AddMinutes(4));

        Assert.Equal(sessionId, completed.Id);
        Assert.Equal(recordingId, completed.RecordingId);
        Assert.Equal(DialogSessionStatus.Completed, completed.Status);
        Assert.Equal(startedAt.AddMinutes(4), completed.EndedAt);
        Assert.Equal(2, completed.Version);
    }

    [Fact]
    public void DialogRecordingGetsSpecificFallbackTitle()
    {
        DateTimeOffset startedAt =
            new(2026, 7, 30, 14, 5, 0, TimeSpan.FromHours(3));

        Recording recording = Recording.Start(RecordingKind.Dialog, startedAt);

        Assert.Equal("AI Dialog · 30 Jul · 14:05", recording.DisplayTitle);
    }
}
