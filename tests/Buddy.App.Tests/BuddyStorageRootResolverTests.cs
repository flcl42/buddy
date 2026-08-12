using Buddy.App.Services;

namespace Buddy.App.Tests;

public sealed class BuddyStorageRootResolverTests
{
    [Fact]
    public void UsesPerUserDataRootAndKeepsLanguageModelsInsideItByDefault()
    {
        string defaultRoot = Path.Combine(
            Path.GetTempPath(),
            "buddy-profile",
            "Buddy");

        BuddyStorageRoots result = BuddyStorageRootResolver.Resolve(
            defaultRoot,
            dataRootOverride: null,
            languageRootOverride: null);

        Assert.Equal(Path.GetFullPath(defaultRoot), result.DataRoot);
        Assert.Equal(
            Path.Combine(Path.GetFullPath(defaultRoot), "language-models"),
            result.LanguageRoot);
    }

    [Fact]
    public void UsesExplicitConfiguredRoots()
    {
        string defaultRoot = Path.Combine(Path.GetTempPath(), "buddy-default");
        string dataRoot = Path.Combine(Path.GetTempPath(), "buddy-data");
        string languageRoot = Path.Combine(Path.GetTempPath(), "buddy-language");

        BuddyStorageRoots result = BuddyStorageRootResolver.Resolve(
            defaultRoot,
            dataRoot,
            languageRoot);

        Assert.Equal(Path.GetFullPath(dataRoot), result.DataRoot);
        Assert.Equal(Path.GetFullPath(languageRoot), result.LanguageRoot);
    }

    [Fact]
    public void TreatsBlankOverridesAsNotConfigured()
    {
        string defaultRoot = Path.Combine(Path.GetTempPath(), "buddy-profile");

        BuddyStorageRoots result = BuddyStorageRootResolver.Resolve(
            defaultRoot,
            dataRootOverride: " ",
            languageRootOverride: "");

        Assert.Equal(Path.GetFullPath(defaultRoot), result.DataRoot);
        Assert.Equal(
            Path.Combine(Path.GetFullPath(defaultRoot), "language-models"),
            result.LanguageRoot);
    }
}
