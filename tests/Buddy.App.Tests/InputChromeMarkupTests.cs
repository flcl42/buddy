using System.Xml.Linq;

namespace Buddy.App.Tests;

public sealed class InputChromeMarkupTests
{
    [Fact]
    public void MainPageDoesNotUseUnstyledNativeInputs()
    {
        XDocument document = XDocument.Load(GetMainPageXamlPath());
        HashSet<string> nativeInputNames =
        [
            "Picker",
            "Entry",
            "Editor",
        ];

        XElement[] nativeInputs = document
            .Descendants()
            .Where(element =>
                nativeInputNames.Contains(element.Name.LocalName) &&
                element.Name.NamespaceName ==
                    "http://schemas.microsoft.com/dotnet/2021/maui")
            .ToArray();

        Assert.Empty(nativeInputs);
    }

    [Fact]
    public void CompositeInputsUseBorderlessNativeChrome()
    {
        XDocument document = XDocument.Load(GetMainPageXamlPath());

        XElement search = document.Descendants().Single(element =>
            element.Name.LocalName == "BorderlessEntry");
        XElement feedback = document.Descendants().Single(element =>
            (string?)element.Attribute("AutomationId") ==
                "FeedbackMessageEditor");

        Assert.Equal("BorderlessEditor", feedback.Name.LocalName);
        Assert.Equal("Border", search.Parent?.Parent?.Name.LocalName);
        Assert.Equal("Border", feedback.Parent?.Name.LocalName);
    }

    private static string GetMainPageXamlPath() => TestRepository.Path(
        "src",
        "Buddy.App",
        "MainPage.xaml");
}
