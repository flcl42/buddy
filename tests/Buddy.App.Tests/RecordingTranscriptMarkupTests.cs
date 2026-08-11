using System.Xml.Linq;

namespace Buddy.App.Tests;

public sealed class RecordingTranscriptMarkupTests
{
    [Fact]
    public void RecordingCardsExposeEditableOnDemandTranscriptControls()
    {
        string path = TestRepository.Path(
            "src",
            "Buddy.App",
            "MainPage.xaml");
        XDocument document = XDocument.Load(path);
        XNamespace maui = "http://schemas.microsoft.com/dotnet/2021/maui";

        Assert.Contains(
            document.Descendants(),
            element => (string?)element.Attribute("AutomationId")
                == "RecordingTranscriptEditor"
                && element.Name.LocalName == "FramedEditor"
                && ((string?)element.Attribute("Text"))?.Contains(
                    "TranscriptText",
                    StringComparison.Ordinal) == true);
        Assert.Contains(
            document.Descendants(maui + "Button"),
            element => (string?)element.Attribute("AutomationId")
                == "TranscribeRecordingButton");
    }
}
