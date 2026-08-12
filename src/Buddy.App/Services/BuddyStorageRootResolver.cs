namespace Buddy.App.Services;

internal static class BuddyStorageRootResolver
{
    public static BuddyStorageRoots Resolve(
        string defaultDataRoot,
        string? dataRootOverride,
        string? languageRootOverride)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultDataRoot);

        string dataRoot = string.IsNullOrWhiteSpace(dataRootOverride)
            ? defaultDataRoot
            : dataRootOverride;
        string languageRoot = string.IsNullOrWhiteSpace(languageRootOverride)
            ? Path.Combine(dataRoot, "language-models")
            : languageRootOverride;

        return new BuddyStorageRoots(
            Path.GetFullPath(dataRoot),
            Path.GetFullPath(languageRoot));
    }
}

internal readonly record struct BuddyStorageRoots(
    string DataRoot,
    string LanguageRoot);
