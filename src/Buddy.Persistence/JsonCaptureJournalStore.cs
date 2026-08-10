using System.Text.Json;
using System.Text.Json.Serialization;
using Buddy.Core.Abstractions;
using Buddy.Core.Domain;

namespace Buddy.Persistence;

public sealed class JsonCaptureJournalStore : ICaptureJournalStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly BuddyDataPaths _paths;

    public JsonCaptureJournalStore(BuddyDataPaths paths)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    }

    public async Task SaveAsync(CaptureJournal journal, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(journal);
        _paths.EnsureCreated();

        string targetPath = GetJournalPath(journal.SessionId);
        string temporaryPath = targetPath + $".{Guid.NewGuid():N}.tmp";

        try
        {
            await using (FileStream stream = new(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             16 * 1024,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                        stream,
                        journal,
                        SerializerOptions,
                        cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, targetPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public async Task<CaptureJournal?> GetAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        string path = GetJournalPath(sessionId);
        if (!File.Exists(path))
        {
            return null;
        }

        await using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            16 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        CaptureJournal? journal = await JsonSerializer.DeserializeAsync<CaptureJournal>(
                stream,
                SerializerOptions,
                cancellationToken)
            .ConfigureAwait(false);

        if (journal is not null && journal.SessionId != sessionId)
        {
            throw new InvalidDataException("Capture journal identifier does not match its file name.");
        }

        return journal;
    }

    public async Task<IReadOnlyList<CaptureJournal>> ListRecoverableAsync(
        CancellationToken cancellationToken = default)
    {
        _paths.EnsureCreated();
        List<CaptureJournal> journals = [];

        foreach (string path in Directory.EnumerateFiles(_paths.CaptureJournals, "*.json", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();

            Guid sessionId;
            if (!Guid.TryParse(Path.GetFileNameWithoutExtension(path), out sessionId))
            {
                continue;
            }

            try
            {
                CaptureJournal? journal = await GetAsync(sessionId, cancellationToken).ConfigureAwait(false);
                if (journal is not null
                    && journal.State is CaptureJournalState.Capturing
                        or CaptureJournalState.Stopping
                        or CaptureJournalState.Interrupted)
                {
                    journals.Add(journal);
                }
            }
            catch (JsonException)
            {
                // A corrupt journal remains on disk for diagnostics and manual recovery.
            }
            catch (InvalidDataException)
            {
                // A mismatched journal remains on disk for diagnostics and manual recovery.
            }
        }

        return journals.OrderBy(journal => journal.StartedAt).ToArray();
    }

    public Task DeleteAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string path = GetJournalPath(sessionId);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    private string GetJournalPath(Guid sessionId)
    {
        return Path.Combine(_paths.CaptureJournals, $"{sessionId:D}.json");
    }
}
