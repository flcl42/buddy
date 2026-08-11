namespace Buddy.Speech;

internal static class KokoroRuntimeAssets
{
    private static readonly string[] RequiredVoiceFiles =
    [
        "af_heart.npy",
        "am_michael.npy",
        "bf_emma.npy",
        "bm_george.npy",
        "ef_dora.npy",
        "ff_siwis.npy",
    ];

    private static readonly Lazy<string> RootPath = new(
        () => FindRootPath(GetRuntimeCandidates()),
        LazyThreadSafetyMode.ExecutionAndPublication);

    public static string GetRootPath()
    {
        return RootPath.Value;
    }

    internal static string FindRootPath(IEnumerable<string?> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        foreach (string? candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(candidate);
            }
            catch (Exception error) when (
                error is ArgumentException
                    or NotSupportedException
                    or PathTooLongException)
            {
                continue;
            }

            if (HasRequiredAssets(fullPath))
            {
                return fullPath;
            }
        }

        throw new DirectoryNotFoundException(
            "Buddy's bundled Kokoro voice and eSpeak files are missing.");
    }

    private static IEnumerable<string?> GetRuntimeCandidates()
    {
        yield return AppContext.BaseDirectory;

        if (AppContext.GetData("NATIVE_DLL_SEARCH_DIRECTORIES") is not string
            nativeSearchDirectories)
        {
            yield break;
        }

        foreach (string path in nativeSearchDirectories.Split(
                     Path.PathSeparator,
                     StringSplitOptions.RemoveEmptyEntries
                         | StringSplitOptions.TrimEntries))
        {
            yield return path;
        }
    }

    private static bool HasRequiredAssets(string rootPath)
    {
        string voicesPath = Path.Combine(rootPath, "voices");
        string espeakPath = Path.Combine(rootPath, "espeak");
        if (!File.Exists(Path.Combine(espeakPath, "espeak-ng-win-amd64.dll"))
            || !Directory.Exists(Path.Combine(espeakPath, "espeak-ng-data")))
        {
            return false;
        }

        return RequiredVoiceFiles.All(
            voiceFile => File.Exists(Path.Combine(voicesPath, voiceFile)));
    }
}
