using Foundation;

namespace Buddy.App;

[Register("AppDelegate")]
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Naming",
    "CA1711",
    Justification = "Apple's MAUI entry point convention requires AppDelegate.")]
public class AppDelegate : MauiUIApplicationDelegate
{
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
