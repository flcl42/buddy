using System.Text.Json;
using System.Text.Json.Serialization;
using Buddy.Core.Domain;

namespace Buddy.App.Services;

internal sealed record DialogAnswerDocument(
    [property: JsonPropertyName("schema_version")] string SchemaVersion,
    [property: JsonPropertyName("display_markdown")] string DisplayMarkdown,
    [property: JsonPropertyName("spoken_text")] string SpokenText);

internal static class DialogAnswerDocumentStore
{
    private const long MaximumDocumentBytes = 256 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public static string GetPath(string recordingDirectory, Guid messageId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recordingDirectory);
        if (messageId == Guid.Empty)
        {
            throw new ArgumentException(
                "A dialog message identifier is required.",
                nameof(messageId));
        }

        return Path.Combine(
            Path.GetFullPath(recordingDirectory),
            $"dialog-message-{messageId:N}.answer.json");
    }

    public static async Task WriteAsync(
        string recordingDirectory,
        Guid messageId,
        string displayMarkdown,
        string spokenText,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayMarkdown);
        ArgumentException.ThrowIfNullOrWhiteSpace(spokenText);
        string destinationPath = GetPath(recordingDirectory, messageId);
        string directory = Path.GetDirectoryName(destinationPath)
            ?? throw new InvalidOperationException(
                "The dialog answer document has no parent directory.");
        Directory.CreateDirectory(directory);

        DialogAnswerDocument document = new(
            ConversationAnswerContract.SchemaVersion,
            displayMarkdown.Trim(),
            spokenText.Trim());
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions);
        if (json.LongLength > MaximumDocumentBytes)
        {
            throw new InvalidDataException(
                "The speaker-aware dialog answer is too large to save safely.");
        }

        string temporaryPath = destinationPath + $".buddy-{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllBytesAsync(temporaryPath, json, cancellationToken)
                .ConfigureAwait(false);
            File.Move(temporaryPath, destinationPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public static async Task<DialogAnswerDocument?> ReadAsync(
        string recordingDirectory,
        Guid messageId,
        CancellationToken cancellationToken = default)
    {
        string path = GetPath(recordingDirectory, messageId);
        if (!File.Exists(path))
        {
            return null;
        }

        FileInfo file = new(path);
        if (file.Length is <= 0 or > MaximumDocumentBytes)
        {
            throw new InvalidDataException(
                "The saved speaker-aware dialog answer has an invalid size.");
        }

        byte[] json = await File.ReadAllBytesAsync(path, cancellationToken)
            .ConfigureAwait(false);
        DialogAnswerDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<DialogAnswerDocument>(
                json,
                JsonOptions);
        }
        catch (JsonException error)
        {
            throw new InvalidDataException(
                "The saved speaker-aware dialog answer is not valid JSON.",
                error);
        }

        if (document is null
            || !string.Equals(
                document.SchemaVersion,
                ConversationAnswerContract.SchemaVersion,
                StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(document.DisplayMarkdown)
            || string.IsNullOrWhiteSpace(document.SpokenText))
        {
            throw new InvalidDataException(
                "The saved speaker-aware dialog answer has an invalid schema.");
        }

        return document with
        {
            DisplayMarkdown = document.DisplayMarkdown.Trim(),
            SpokenText = document.SpokenText.Trim(),
        };
    }
}
