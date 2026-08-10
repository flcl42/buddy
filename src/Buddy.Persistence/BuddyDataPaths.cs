namespace Buddy.Persistence;

public sealed class BuddyDataPaths
{
    public BuddyDataPaths(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        Root = Path.GetFullPath(rootPath);
        DatabasePath = Path.Combine(Root, "buddy.db");
        Recordings = Path.Combine(Root, "recordings");
        Models = Path.Combine(Root, "models");
        CaptureJournals = Path.Combine(Root, "capture-journal");
        DialogWork = Path.Combine(Root, "dialog-work");
        SpeechCache = Path.Combine(Root, "speech-cache");
        Logs = Path.Combine(Root, "logs");
        Backups = Path.Combine(Root, "backups");
    }

    public string Root { get; }

    public string DatabasePath { get; }

    public string Recordings { get; }

    public string Models { get; }

    public string CaptureJournals { get; }

    public string DialogWork { get; }

    public string SpeechCache { get; }

    public string Logs { get; }

    public string Backups { get; }

    public void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Recordings);
        Directory.CreateDirectory(Models);
        Directory.CreateDirectory(CaptureJournals);
        Directory.CreateDirectory(DialogWork);
        Directory.CreateDirectory(SpeechCache);
        Directory.CreateDirectory(Logs);
        Directory.CreateDirectory(Backups);
    }

    public string GetRecordingDirectory(Guid recordingId, DateTimeOffset createdAt)
    {
        string directory = Path.Combine(
            Recordings,
            createdAt.Year.ToString("D4", System.Globalization.CultureInfo.InvariantCulture),
            createdAt.Month.ToString("D2", System.Globalization.CultureInfo.InvariantCulture),
            recordingId.ToString("D"));

        return EnsureInsideRoot(directory, Recordings);
    }

    public string GetCaptureSessionDirectory(Guid sessionId)
    {
        return EnsureInsideRoot(Path.Combine(CaptureJournals, sessionId.ToString("D")), CaptureJournals);
    }

    public string GetDialogWorkDirectory(Guid sessionId)
    {
        return EnsureInsideRoot(
            Path.Combine(DialogWork, sessionId.ToString("D")),
            DialogWork);
    }

    public string ResolveRecordingArtifact(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        return EnsureInsideRoot(Path.Combine(Recordings, relativePath), Recordings);
    }

    public string ToRecordingRelativePath(string absolutePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absolutePath);
        string safePath = EnsureInsideRoot(absolutePath, Recordings);
        return Path.GetRelativePath(Recordings, safePath);
    }

    private static string EnsureInsideRoot(string candidate, string root)
    {
        string fullRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        string fullCandidate = Path.GetFullPath(candidate);

        if (!fullCandidate.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(
                fullCandidate.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                fullRoot.TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Resolved path is outside the configured Buddy data root.");
        }

        return fullCandidate;
    }
}
