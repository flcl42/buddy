using System.Xml.Linq;

namespace Buddy.App.Tests;

public sealed class SpeakModeChooserMarkupTests
{
    [Fact]
    public void SpeakTabUsesTwoLargeChoicesInsteadOfHeaderModeButtons()
    {
        XDocument document = XDocument.Load(GetMainPageXamlPath());
        XNamespace maui = "http://schemas.microsoft.com/dotnet/2021/maui";

        XElement chooser = FindByAutomationId(
            document,
            maui + "Grid",
            "SpeakModeChooser");
        XElement dialogPanel = FindByAutomationId(
            chooser,
            maui + "Border",
            "DialogChoicePanel");
        XElement monologuePanel = FindByAutomationId(
            chooser,
            maui + "Border",
            "MonologueChoicePanel");
        XElement dialogButton = FindByAutomationId(
            dialogPanel,
            maui + "Button",
            "StartDialogChoiceButton");
        XElement monologueButton = FindByAutomationId(
            monologuePanel,
            maui + "Button",
            "StartMonologueChoiceButton");

        Assert.Equal(
            "{Binding IsSpeakModeChooserVisible}",
            (string?)chooser.Attribute("IsVisible"));
        Assert.Equal("0", (string?)dialogPanel.Attribute("Grid.Column"));
        Assert.Equal("2", (string?)monologuePanel.Attribute("Grid.Column"));
        Assert.Equal(
            "{Binding StartDialogFromChooserCommand}",
            (string?)dialogButton.Attribute("Command"));
        Assert.Equal(
            "{Binding StartMonologueFromChooserCommand}",
            (string?)monologueButton.Attribute("Command"));
        Assert.DoesNotContain(
            document.Descendants(),
            element =>
                (string?)element.Attribute("AutomationId")
                    is "DialogModeButton" or "MonologueModeButton");
    }

    private static XElement FindByAutomationId(
        XContainer container,
        XName elementName,
        string automationId) =>
        container
            .Descendants(elementName)
            .Single(element =>
                (string?)element.Attribute("AutomationId") == automationId);

    private static string GetMainPageXamlPath() => TestRepository.Path(
        "src",
        "Buddy.App",
        "MainPage.xaml");
}
