using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Buddy.App.ViewModels;
using Buddy.Core.Domain;

namespace Buddy.App.Tests;

public sealed class DialogMessageCollectionReconcilerTests
{
    [Fact]
    public void IdenticalSnapshotPreservesItemsWithoutCollectionChanges()
    {
        DialogMessage[] source = CreateMessages(6);
        ObservableCollection<DialogMessageViewModel> target = new(
            source.Select(message => new DialogMessageViewModel(message, false)));
        DialogMessageViewModel[] originalItems = target.ToArray();
        int collectionChanges = 0;
        target.CollectionChanged += (_, _) => collectionChanges++;

        DialogMessageCollectionReconciler.Reconcile(
            target,
            source);

        Assert.Equal(0, collectionChanges);
        Assert.Equal(originalItems.Length, target.Count);
        Assert.All(
            originalItems,
            (item, index) => Assert.Same(item, target[index]));
    }

    [Fact]
    public void AudioAttachmentUpdatesItemWithoutResettingCollection()
    {
        DialogMessage[] source = CreateMessages(2);
        ObservableCollection<DialogMessageViewModel> target = new(
            source.Select(message => new DialogMessageViewModel(message, false)));
        DialogMessageViewModel assistant = target[1];
        Guid artifactId = Guid.NewGuid();
        DialogMessage[] updated =
        [
            source[0],
            source[1].WithAudioArtifact(artifactId),
        ];
        int collectionChanges = 0;
        target.CollectionChanged += (_, _) => collectionChanges++;

        DialogMessageCollectionReconciler.Reconcile(
            target,
            updated);

        Assert.Equal(0, collectionChanges);
        Assert.Same(assistant, target[1]);
        Assert.Equal(artifactId, target[1].AudioArtifactId);
        Assert.True(target[1].HasAudio);
        Assert.Equal(AudioTransportState.Idle, target[1].MessageAudioState);
    }

    [Fact]
    public void NewTurnAppendsOnlyTheNewTail()
    {
        DialogMessage[] initial = CreateMessages(4);
        DialogMessage[] updated = [.. initial, CreateMessage(4)];
        ObservableCollection<DialogMessageViewModel> target = new(
            initial.Select(message => new DialogMessageViewModel(message, false)));
        DialogMessageViewModel[] originalItems = target.ToArray();
        List<NotifyCollectionChangedAction> actions = [];
        target.CollectionChanged += (_, eventArgs) => actions.Add(eventArgs.Action);

        DialogMessageCollectionReconciler.Reconcile(
            target,
            updated);

        Assert.Equal([NotifyCollectionChangedAction.Add], actions);
        Assert.Equal(5, target.Count);
        Assert.All(
            originalItems,
            (item, index) => Assert.Same(item, target[index]));
        Assert.Equal(updated[^1].Id, target[^1].Id);
    }

    [Fact]
    public void PronunciationUpdateKeepsExistingMessageItem()
    {
        DialogMessage[] source = CreateMessages(2);
        ObservableCollection<DialogMessageViewModel> target = new(
            source.Select(message => new DialogMessageViewModel(message, false)));
        DialogMessageViewModel user = target[0];
        DialogPronunciationAssessment assessment = new(
            source[0].Id,
            source[0].Text,
            "mˈɛsɪdʒ",
            DateTimeOffset.UtcNow,
            "Whisper",
            "buddy.pronunciation.v2",
            [
                new(
                    source[0].Id,
                    0,
                    "Message",
                    TimeSpan.Zero,
                    TimeSpan.FromMilliseconds(300),
                    0.48f),
            ]);
        int collectionChanges = 0;
        target.CollectionChanged += (_, _) => collectionChanges++;

        DialogMessageCollectionReconciler.Reconcile(
            target,
            source,
            pronunciations: new Dictionary<Guid, DialogPronunciationAssessment>
            {
                [source[0].Id] = assessment,
            });

        Assert.Equal(0, collectionChanges);
        Assert.Same(user, target[0]);
        Assert.True(user.HasPronunciation);
        Assert.True(user.HasPronunciationWords);
        Assert.Single(user.PronunciationWords);
        Assert.Contains("mˈɛsɪdʒ", user.PhoneticTranscriptText);
    }

    [Fact]
    public void SnapshotReconciliationPreservesPausedTransportState()
    {
        DialogMessage[] source = CreateMessages(2);
        ObservableCollection<DialogMessageViewModel> target = new(
            source.Select(message => new DialogMessageViewModel(message, false)));
        DialogMessageViewModel assistant = target[1];
        assistant.AudioArtifactId = Guid.NewGuid();
        assistant.MessageAudioState = AudioTransportState.Paused;

        DialogMessageCollectionReconciler.Reconcile(target, source);

        Assert.Same(assistant, target[1]);
        Assert.Equal(
            AudioTransportState.Paused,
            target[1].MessageAudioState);
    }

    private static DialogMessage[] CreateMessages(int count)
    {
        return Enumerable.Range(0, count).Select(CreateMessage).ToArray();
    }

    private static DialogMessage CreateMessage(int sequence)
    {
        DialogMessageRole role = sequence % 2 == 0
            ? DialogMessageRole.User
            : DialogMessageRole.Assistant;
        return new DialogMessage(
            Guid.NewGuid(),
            Guid.Parse("2f3447d7-fc86-474a-80fc-540e21cf571d"),
            sequence,
            role,
            $"Message {sequence}",
            new DateTimeOffset(2026, 7, 30, 12, sequence, 0, TimeSpan.Zero),
            role == DialogMessageRole.User ? "Whisper.net" : "DeepSeek",
            role == DialogMessageRole.Assistant ? "deepseek-chat" : null,
            null,
            null,
            null,
            null);
    }
}
