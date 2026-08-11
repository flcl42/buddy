using System.Runtime.Versioning;
using Microsoft.Maui.Platforms.Linux.Gtk4.Platform;

namespace Buddy.App;

[SupportedOSPlatform("linux")]
public sealed class Program : GtkMauiApplication
{
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

    public static void Main(string[] args)
    {
        Program app = new();
        app.Run(args);
    }
}
