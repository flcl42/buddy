using System.Xml.Linq;

namespace Buddy.App.Tests;

public sealed class FeedbackOverlayMarkupTests
{
    [Fact]
    public void FeedbackModalIsDismissibleAndSupportsOneOptionalScreenshot()
    {
        XDocument document = XDocument.Load(FindMainPage());
        XElement overlay = Assert.Single(
            document.Descendants(),
            element => (string?)element.Attribute("AutomationId")
                == "FeedbackOverlay");
        XElement backdrop = Assert.Single(
            overlay.Descendants(),
            element => (string?)element.Attribute("AutomationId")
                == "FeedbackBackdrop");
        XElement panel = Assert.Single(
            overlay.Descendants(),
            element => (string?)element.Attribute("AutomationId")
                == "FeedbackPanel");
        XElement editor = Assert.Single(
            panel.Descendants(),
            element => (string?)element.Attribute("AutomationId")
                == "FeedbackMessageEditor");

        Assert.Equal("{Binding Feedback.IsOpen}", (string?)overlay.Attribute("IsVisible"));
        Assert.Equal("False", (string?)panel.Attribute("InputTransparent"));
        Assert.Equal("3000", (string?)editor.Attribute("MaxLength"));
        Assert.Contains(
            backdrop.Descendants(),
            element => (string?)element.Attribute("Command")
                == "{Binding Feedback.CloseCommand}");
        Assert.Single(
            panel.Descendants(),
            element => (string?)element.Attribute("AutomationId")
                == "FeedbackAttachScreenshotButton");
        Assert.Single(
            panel.Descendants(),
            element => (string?)element.Attribute("AutomationId")
                == "FeedbackSendButton");
    }

    [Fact]
    public void HeaderAndTrayUseVisibilitySafeFeedbackCommands()
    {
        XDocument document = XDocument.Load(FindMainPage());
        IReadOnlyList<string?> commands = document
            .Descendants()
            .Select(element => (string?)element.Attribute("Command"))
            .ToArray();

        Assert.Single(
            commands,
            command => command == "{Binding OpenFeedbackCommand}");
        Assert.Single(
            commands,
            command => command == "{Binding OpenFeedbackWindowCommand}");
    }

    private static string FindMainPage()
    {
        string current = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(current))
        {
            string candidate = Path.Combine(current, "src", "Buddy.App", "MainPage.xaml");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = Directory.GetParent(current)?.FullName ?? string.Empty;
        }

        throw new FileNotFoundException("Could not locate Buddy MainPage.xaml.");
    }
}
