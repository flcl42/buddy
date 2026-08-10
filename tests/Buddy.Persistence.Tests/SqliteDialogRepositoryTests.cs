using Buddy.Core.Domain;
using Microsoft.Data.Sqlite;

namespace Buddy.Persistence.Tests;

public sealed class SqliteDialogRepositoryTests
{
    [Fact]
    public async Task SessionMessagesAndAnswerAudioRoundTripInSequence()
    {
        await using TemporaryBuddyStore store = await TemporaryBuddyStore.CreateAsync();
        SqliteRecordingRepository recordings = new(store.Connections);
        SqliteDialogRepository dialogs = new(store.Connections);
        DateTimeOffset startedAt =
            new(2026, 7, 30, 13, 0, 0, TimeSpan.FromHours(3));
        Recording recording = Recording.Start(
            RecordingKind.Dialog,
            startedAt,
            id: Guid.NewGuid());
        await recordings.AddAsync(recording);
        DialogSession session = DialogSession.Start(
            recording.Id,
            startedAt,
            "Keep the complete ordered context.",
            Guid.NewGuid());
        await dialogs.AddSessionAsync(session);

        DialogMessage user = new(
            Guid.NewGuid(),
            session.Id,
            0,
            DialogMessageRole.User,
            "What did I ask earlier?",
            startedAt.AddSeconds(2),
            "Whisper.net",
            "large-v3-turbo",
            null,
            null,
            null,
            null);
        DialogMessage assistant = new(
            Guid.NewGuid(),
            session.Id,
            1,
            DialogMessageRole.Assistant,
            "You asked about retaining the full dialog context.",
            startedAt.AddSeconds(3),
            "deepseek",
            "deepseek-v4-flash",
            TimeSpan.FromMilliseconds(420),
            120,
            36,
            null);
        DialogPronunciationAssessment pronunciation = new(
            user.Id,
            user.Text,
            "wʌt dɪd aɪ æsk ˈɜːliɚ",
            startedAt.AddSeconds(2),
            "large-v3-turbo",
            "buddy.pronunciation.v2",
            [
                new(
                    user.Id,
                    0,
                    "What",
                    TimeSpan.Zero,
                    TimeSpan.FromMilliseconds(220),
                    0.91f),
                new(
                    user.Id,
                    1,
                    "did",
                    TimeSpan.FromMilliseconds(240),
                    TimeSpan.FromMilliseconds(390),
                    0.68f),
            ]);
        await dialogs.AddUserMessageWithPronunciationAsync(user, pronunciation);
        await dialogs.AddMessageAsync(assistant);

        AudioArtifact answerAudio = new(
            Guid.NewGuid(),
            recording.Id,
            AudioArtifactKind.DialogAssistant,
            Path.Combine(
                "2026",
                "07",
                recording.Id.ToString("D"),
                "dialog-answer.wav"),
            AudioContainer.Wave,
            24_000,
            1,
            TimeSpan.FromSeconds(2),
            48_000,
            new string('a', 64),
            "kokoro; dialog-message=" + assistant.Id.ToString("D"),
            startedAt.AddSeconds(4));
        await recordings.AddAudioArtifactAsync(answerAudio);
        await dialogs.UpdateMessageAudioAsync(assistant.Id, answerAudio.Id);

        IReadOnlyList<DialogMessage> messages =
            await dialogs.GetMessagesAsync(session.Id);

        Assert.Equal(2, messages.Count);
        Assert.Equal(user, messages[0]);
        Assert.Equal(assistant.WithAudioArtifact(answerAudio.Id), messages[1]);
        Assert.Equal(session, await dialogs.GetActiveSessionAsync());
        Assert.Equal(session, await dialogs.GetLatestSessionAsync());
        Assert.Equal(session, await dialogs.GetSessionAsync(session.Id));
        Assert.Equal(
            session,
            await dialogs.GetSessionByRecordingIdAsync(recording.Id));
        IReadOnlyDictionary<Guid, DialogPronunciationAssessment> assessments =
            await dialogs.GetPronunciationAssessmentsAsync(session.Id);
        DialogPronunciationAssessment loaded = Assert.Single(assessments).Value;
        Assert.Equal(pronunciation.MessageId, loaded.MessageId);
        Assert.Equal(pronunciation.Transcript, loaded.Transcript);
        Assert.Equal(
            pronunciation.PhoneticTranscript,
            loaded.PhoneticTranscript);
        Assert.Equal(pronunciation.Words, loaded.Words);
    }

    [Fact]
    public async Task CompletionUsesOptimisticConcurrencyAndDialogTextIsSearchable()
    {
        await using TemporaryBuddyStore store = await TemporaryBuddyStore.CreateAsync();
        SqliteRecordingRepository recordings = new(store.Connections);
        SqliteDialogRepository dialogs = new(store.Connections);
        DateTimeOffset startedAt = DateTimeOffset.UtcNow;
        Recording recording = Recording.Start(
            RecordingKind.Dialog,
            startedAt,
            id: Guid.NewGuid());
        await recordings.AddAsync(recording);
        DialogSession session = DialogSession.Start(
            recording.Id,
            startedAt,
            "Retain context.",
            Guid.NewGuid());
        await dialogs.AddSessionAsync(session);
        await dialogs.AddMessageAsync(
            new DialogMessage(
                Guid.NewGuid(),
                session.Id,
                0,
                DialogMessageRole.User,
                "Explain Kademlia bucket refresh behavior.",
                startedAt.AddSeconds(1),
                "Whisper.net",
                null,
                null,
                null,
                null,
                null));

        DialogSession completing = session.BeginCompletion();
        Assert.True(await dialogs.TryUpdateSessionAsync(
            completing,
            expectedVersion: session.Version));
        Assert.False(await dialogs.TryUpdateSessionAsync(
            completing.WithError("stale"),
            expectedVersion: session.Version));
        DialogSession completed = completing.Complete(startedAt.AddMinutes(1));
        Assert.True(await dialogs.TryUpdateSessionAsync(
            completed,
            expectedVersion: completing.Version));

        Assert.Null(await dialogs.GetActiveSessionAsync());
        Assert.Equal(completed, await dialogs.GetLatestSessionAsync());
        IReadOnlyList<Recording> results = await recordings.ListAsync(
            new RecordingQuery(Search: "Kademlia bucket"));
        Assert.Single(results);
        Assert.Equal(recording.Id, results[0].Id);
    }

    [Fact]
    public async Task DatabaseAllowsOnlyOneActiveDialog()
    {
        await using TemporaryBuddyStore store = await TemporaryBuddyStore.CreateAsync();
        SqliteRecordingRepository recordings = new(store.Connections);
        SqliteDialogRepository dialogs = new(store.Connections);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        Recording first = Recording.Start(
            RecordingKind.Dialog,
            now,
            id: Guid.NewGuid());
        Recording second = Recording.Start(
            RecordingKind.Dialog,
            now.AddSeconds(1),
            id: Guid.NewGuid());
        await recordings.AddAsync(first);
        await recordings.AddAsync(second);
        await dialogs.AddSessionAsync(
            DialogSession.Start(first.Id, now, "First active dialog."));

        await Assert.ThrowsAsync<SqliteException>(
            () => dialogs.AddSessionAsync(
                DialogSession.Start(
                    second.Id,
                    now.AddSeconds(1),
                    "Second active dialog.")));
    }
}
