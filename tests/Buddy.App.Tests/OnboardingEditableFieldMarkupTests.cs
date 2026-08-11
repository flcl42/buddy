using System.Runtime.CompilerServices;
using System.Xml.Linq;

namespace Buddy.App.Tests;

public sealed class OnboardingEditableFieldMarkupTests
{
    [Fact]
    public void SetupInputsUseSingleRoundedNativeControlChrome()
    {
        XDocument document = XDocument.Load(GetMainPageXamlPath());
        XNamespace maui = "http://schemas.microsoft.com/dotnet/2021/maui";

        string[] fieldIds =
        [
            "OnboardingInterfaceLanguagePicker",
            "OnboardingDialogLanguagePicker",
            "OnboardingProviderPicker",
            "OnboardingTrialCodeEntry",
            "OnboardingDeepSeekKeyEntry",
        ];

        foreach (string fieldId in fieldIds)
        {
            XElement field = document
                .Descendants()
                .Single(element =>
                    (string?)element.Attribute("AutomationId") == fieldId);
            string expectedControl = fieldId.EndsWith("Picker", StringComparison.Ordinal)
                ? "FramedPicker"
                : "FramedEntry";
            string expectedStyle = fieldId.EndsWith("Picker", StringComparison.Ordinal)
                ? "{StaticResource OnboardingPicker}"
                : "{StaticResource OnboardingEntry}";

            Assert.Equal(expectedControl, field.Name.LocalName);
            Assert.Equal(expectedStyle, (string?)field.Attribute("Style"));
            Assert.NotEqual("Border", field.Parent?.Name.LocalName);
        }

        AssertInputStyle(document, maui, "OnboardingPicker", "FramedPicker");
        AssertInputStyle(document, maui, "OnboardingEntry", "FramedEntry");
    }

    private static void AssertInputStyle(
        XDocument document,
        XNamespace maui,
        string styleKey,
        string targetType)
    {
        XElement style = document
            .Descendants(maui + "Style")
            .Single(element =>
                (string?)element.Attribute(XName.Get(
                    "Key",
                    "http://schemas.microsoft.com/winfx/2009/xaml")) == styleKey);
        Dictionary<string, string?> setters = style
            .Elements(maui + "Setter")
            .ToDictionary(
                setter => (string)setter.Attribute("Property")!,
                setter => (string?)setter.Attribute("Value"));

        Assert.Equal($"controls:{targetType}", (string?)style.Attribute("TargetType"));
        Assert.Equal("#FFFFFF", setters["BackgroundColor"]);
        Assert.Equal("46", setters["HeightRequest"]);
        Assert.Equal("#222538", setters["TextColor"]);
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
