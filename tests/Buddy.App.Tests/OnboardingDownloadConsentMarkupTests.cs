using System.Xml.Linq;

namespace Buddy.App.Tests;

public sealed class OnboardingDownloadConsentMarkupTests
{
    [Fact]
    public void WelcomeScreenExposesOneExplicitSetupDownloadAction()
    {
        XDocument document = XDocument.Load(GetMainPageXamlPath());
        XNamespace maui = "http://schemas.microsoft.com/dotnet/2021/maui";

        XElement consent = FindByAutomationId(
            document,
            maui + "Border",
            "OnboardingDownloadConsent");
        XElement setupButton = FindByAutomationId(
            document,
            maui + "Button",
            "OnboardingSetupButton");
        XElement[] setupCommandElements = document
            .Descendants()
            .Where(element =>
                (string?)element.Attribute("Command")
                    == "{Binding Onboarding.RunSetupCommand}")
            .ToArray();

        Assert.Contains(
            consent.Descendants(maui + "Label"),
            label =>
                (string?)label.Attribute("Text")
                    == "{DynamicResource SetupDownloadConsent}");
        Assert.Equal(
            "{Binding Onboarding.RunSetupCommand}",
            (string?)setupButton.Attribute("Command"));
        Assert.Equal(
            "{Binding Onboarding.SetupButtonText}",
            (string?)setupButton.Attribute("Text"));
        Assert.Single(setupCommandElements);
        Assert.Same(setupButton, setupCommandElements[0]);
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
