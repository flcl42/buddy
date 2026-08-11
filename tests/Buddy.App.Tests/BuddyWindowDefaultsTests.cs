using System.Runtime.CompilerServices;

namespace Buddy.App.Tests;

public sealed class BuddyWindowDefaultsTests
{
    [Fact]
    public void DefaultWindowIsLargeEnoughForTheSetupWizard()
    {
        string source = File.ReadAllText(GetBuddyWindowPath());

        Assert.Contains("Width = 1_260;", source, StringComparison.Ordinal);
        Assert.Contains("Height = 830;", source, StringComparison.Ordinal);
        Assert.Contains("MinimumWidth = 900;", source, StringComparison.Ordinal);
        Assert.Contains("MinimumHeight = 640;", source, StringComparison.Ordinal);
    }

    private static string GetBuddyWindowPath(
        [CallerFilePath] string testFilePath = "") =>
        Path.GetFullPath(
            Path.Combine(
                Path.GetDirectoryName(testFilePath)!,
                "..",
                "..",
                "src",
                "Buddy.App",
                "BuddyWindow.cs"));
}
