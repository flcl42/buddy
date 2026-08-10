using Buddy.App.Services;
using Buddy.Core.Domain;

namespace Buddy.App.Tests;

public sealed class DialogAnswerDocumentStoreTests
{
    [Fact]
    public async Task RoundTripPreservesFormattedAndSpokenRepresentations()
    {
        string directory = CreateTemporaryDirectory();
        Guid messageId = Guid.NewGuid();
        try
        {
            await DialogAnswerDocumentStore.WriteAsync(
                directory,
                messageId,
                "## Result\n\nUse **Buddy**.",
                "Result. Use Buddy.");

            DialogAnswerDocument document = Assert.IsType<DialogAnswerDocument>(
                await DialogAnswerDocumentStore.ReadAsync(directory, messageId));

            Assert.Equal(
                ConversationAnswerContract.SchemaVersion,
                document.SchemaVersion);
            Assert.Equal("## Result\n\nUse **Buddy**.", document.DisplayMarkdown);
            Assert.Equal("Result. Use Buddy.", document.SpokenText);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ReadReturnsNullWhenAnOlderAnswerHasNoDocument()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            Assert.Null(
                await DialogAnswerDocumentStore.ReadAsync(
                    directory,
                    Guid.NewGuid()));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ReadRejectsAnUnversionedDocument()
    {
        string directory = CreateTemporaryDirectory();
        Guid messageId = Guid.NewGuid();
        try
        {
            string path = DialogAnswerDocumentStore.GetPath(directory, messageId);
            await File.WriteAllTextAsync(
                path,
                "{\"display_markdown\":\"Visible\",\"spoken_text\":\"Spoken\"}");

            InvalidDataException error = await Assert.ThrowsAsync<InvalidDataException>(
                () => DialogAnswerDocumentStore.ReadAsync(directory, messageId));

            Assert.Contains("schema", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "buddy-answer-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
