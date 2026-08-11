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

    private static string GetBuddyWindowPath() => TestRepository.Path(
        "src",
        "Buddy.App",
        "BuddyWindow.cs");
}
