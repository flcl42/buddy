using System.Runtime.CompilerServices;
using System.Xml.Linq;

namespace Buddy.App.Tests;

public sealed class SettingsOverlayMarkupTests
{
    [Fact]
    public void BackdropTapClosesSettingsWithoutCoveringThePanel()
    {
        XDocument document = XDocument.Load(GetMainPageXamlPath());
        XNamespace maui = "http://schemas.microsoft.com/dotnet/2021/maui";

        XElement overlay = document
            .Descendants(maui + "Grid")
            .Single(element =>
                (string?)element.Attribute("AutomationId") == "SettingsOverlay");
        XElement backdrop = overlay
            .Elements(maui + "BoxView")
            .Single(element =>
                (string?)element.Attribute("AutomationId") == "SettingsBackdrop");
        XElement panel = overlay
            .Elements(maui + "Border")
            .Single(element =>
                (string?)element.Attribute("AutomationId") == "SettingsPanel");

        XElement dismissGesture = backdrop
            .Descendants(maui + "TapGestureRecognizer")
            .Single();

        Assert.Equal(
            "{Binding CloseSettingsCommand}",
            (string?)dismissGesture.Attribute("Command"));
        Assert.Equal("False", (string?)panel.Attribute("InputTransparent"));
        Assert.DoesNotContain(
            panel.Descendants(maui + "TapGestureRecognizer"),
            gesture =>
                (string?)gesture.Attribute("Command")
                    == "{Binding CloseSettingsCommand}");
    }

    private static string GetMainPageXamlPath(
        [CallerFilePath] string testFilePath = "") =>
        Path.GetFullPath(
            Path.Combine(
                Path.GetDirectoryName(testFilePath)!,
                "..",
                "..",
                "src",
                "Buddy.App",
                "MainPage.xaml"));
}
