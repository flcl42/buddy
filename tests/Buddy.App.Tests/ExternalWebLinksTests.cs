using System.Xml.Linq;
using Buddy.App.Services;

namespace Buddy.App.Tests;

public sealed class ExternalWebLinksTests
{
    [Fact]
    public void PublicLinksUseCanonicalHttpsPages()
    {
        Assert.Equal(
            "https://flcl42.github.io/buddy/deepseek-api-key/",
            BuddyWebLinks.DeepSeekApiKeyGuide.AbsoluteUri);
        Assert.Equal(
            "https://flcl42.github.io/buddy/privacy/",
            BuddyWebLinks.PrivacyPolicy.AbsoluteUri);
    }

    [Fact]
    public void SetupAndSettingsExposeTheExpectedLinkCommands()
    {
        XDocument document = XDocument.Load(TestRepository.Path(
            "src",
            "Buddy.App",
            "MainPage.xaml"));

        AssertCommand(
            document,
            "DeepSeekApiKeyGuideButton",
            "{Binding Settings.OpenDeepSeekApiKeyGuideCommand}");
        AssertCommand(
            document,
            "OnboardingDeepSeekApiKeyGuideButton",
            "{Binding Settings.OpenDeepSeekApiKeyGuideCommand}");
        AssertCommand(
            document,
            "PrivacyPolicyButton",
            "{Binding Settings.OpenPrivacyPolicyCommand}");
    }

    private static void AssertCommand(
        XDocument document,
        string automationId,
        string expectedCommand)
    {
        XElement element = document
            .Descendants()
            .Single(candidate =>
                (string?)candidate.Attribute("AutomationId") == automationId);

        Assert.Equal(expectedCommand, (string?)element.Attribute("Command"));
    }
}
