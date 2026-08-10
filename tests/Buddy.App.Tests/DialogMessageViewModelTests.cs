using Buddy.App.ViewModels;
using Buddy.Core.Domain;

namespace Buddy.App.Tests;

public sealed class DialogMessageViewModelTests
{
    [Fact]
    public void AssistantMessagePreparesMarkdownForRendering()
    {
        DialogMessageViewModel message = CreateAssistant(
            "A **clear** reply with [context](https://example.test). ");

        Assert.Equal("A clear reply with context.", message.PlainText);
        Assert.Contains(
            message.RenderedContent.Blocks,
            block => block.Runs.Any(
                run => run.Text == "clear"
                    && run.Style.HasFlag(MarkdownInlineStyle.Bold)));
    }

    [Fact]
    public void SavedMessageRetainsRecordingContextAndFullMarkdown()
    {
        Guid recordingId = Guid.NewGuid();
        const string markdown = "## Decision\n\nKeep **all** context and `formatting`.";
        DialogMessage persisted = new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            3,
            DialogMessageRole.Assistant,
            markdown,
            DateTimeOffset.UtcNow,
            "qwen",
            "Qwen3.6-27B",
            TimeSpan.FromSeconds(1),
            20,
            12,
            null);

        DialogMessageViewModel message = new(
            persisted,
            isPlaying: false,
            pronunciation: null,
            recordingId);

        Assert.Equal(recordingId, message.RecordingId);
        Assert.Equal(markdown, message.Text);
        Assert.Equal(
            $"Decision{Environment.NewLine}Keep all context and formatting.",
            message.PlainText);
        Assert.Contains(
            message.RenderedContent.Blocks,
            block => block.Kind == MarkdownBlockKind.Heading);
    }

    [Fact]
    public void UserMessageExposesTheUnifiedAudioTransport()
    {
        DialogMessageViewModel message = CreateUser(
            "I used **nuanced** in my reply.");

        Assert.True(message.IsUser);
        Assert.Equal("I used nuanced in my reply.", message.PlainText);
        Assert.NotEmpty(message.RenderedContent.Blocks);
        Assert.True(message.HasMessageAudioControl);
        Assert.True(message.CanControlMessageAudio);
        Assert.Equal(AudioTransportState.Idle, message.MessageAudioState);
        Assert.Equal("your reply", message.MessageAudioSubject);

        message.MessageAudioState = AudioTransportState.Preparing;
        Assert.False(message.CanControlMessageAudio);
        message.MessageAudioState = AudioTransportState.Playing;
        Assert.True(message.CanControlMessageAudio);
        message.MessageAudioState = AudioTransportState.Paused;
        Assert.True(message.CanControlMessageAudio);
    }

    [Fact]
    public void AssistantTransportAppearsWhenNarrationIsAttached()
    {
        DialogMessageViewModel message = CreateAssistant("A concise answer.");

        Assert.False(message.HasMessageAudioControl);
        Assert.False(message.CanControlMessageAudio);

        message.AudioArtifactId = Guid.NewGuid();

        Assert.True(message.HasMessageAudioControl);
        Assert.True(message.CanControlMessageAudio);
        Assert.Equal("AI answer", message.MessageAudioSubject);
    }

    [Fact]
    public void WordDetailsAreCachedOnlyAfterBothLazyResultsComplete()
    {
        DialogMessageViewModel message = CreateAssistant("A precise answer.");
        WordDefinitionResult definition = new(
            "precise",
            "adjective",
            "Exact and carefully expressed.",
            "deepseek",
            "deepseek-v4-flash",
            TimeSpan.FromMilliseconds(80));

        Assert.False(message.TryShowCachedWordLookup("precise"));
        message.BeginWordLookup("precise");
        Assert.True(message.IsWordLookupVisible);
        Assert.True(message.IsWordLookupLoading);

        message.ApplyWordDefinition("precise", definition);
        Assert.True(message.IsWordLookupLoading);
        Assert.Equal(definition.Definition, message.WordDefinitionText);

        message.ApplyWordPhonetic("precise", "pɹɪsˈaɪs");
        Assert.False(message.IsWordLookupLoading);
        Assert.Equal("/pɹɪsˈaɪs/", message.WordPhoneticText);
        Assert.True(message.CanControlWordAudio);
        Assert.Equal("word precise", message.WordAudioSubject);
        Assert.Equal(AudioTransportState.Idle, message.WordAudioState);

        message.WordAudioState = AudioTransportState.Preparing;
        Assert.False(message.CanControlWordAudio);
        message.WordAudioState = AudioTransportState.Playing;
        Assert.True(message.CanControlWordAudio);
        message.WordAudioState = AudioTransportState.Paused;
        Assert.True(message.CanControlWordAudio);

        message.DismissWordLookupCommand.Execute(null);
        Assert.False(message.IsWordLookupVisible);
        Assert.True(message.TryShowCachedWordLookup("PRECISE"));
        Assert.True(message.IsWordLookupVisible);
        Assert.Equal(definition.Definition, message.WordDefinitionText);
        Assert.Equal("adjective", message.WordPartOfSpeechText);
    }

    private static DialogMessageViewModel CreateAssistant(string text)
    {
        DialogMessage message = new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            1,
            DialogMessageRole.Assistant,
            text,
            DateTimeOffset.UtcNow,
            "deepseek",
            "deepseek-v4-flash",
            TimeSpan.FromMilliseconds(100),
            20,
            10,
            null);
        return new DialogMessageViewModel(message, false);
    }

    private static DialogMessageViewModel CreateUser(string text)
    {
        DialogMessage message = new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            1,
            DialogMessageRole.User,
            text,
            DateTimeOffset.UtcNow,
            "local",
            "whisper-large-v3-turbo",
            TimeSpan.FromMilliseconds(100),
            null,
            null,
            null);
        return new DialogMessageViewModel(message, false);
    }
}
