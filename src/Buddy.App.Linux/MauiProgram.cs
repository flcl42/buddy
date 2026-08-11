using System.Runtime.Versioning;
using Buddy.App.Services;
using Buddy.Speech;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Platforms.Linux.Gtk4.Hosting;

namespace Buddy.App;

[SupportedOSPlatform("linux")]
public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        MauiAppBuilder builder = MauiApp
            .CreateBuilder()
            .UseMauiAppLinuxGtk4<App>();
#if DEBUG
        builder.Logging.AddDebug();
#endif
        BuddyServiceRegistration.AddBuddyServices(builder.Services);
        builder.Services.AddSingleton<IDesktopTrayService, LinuxDesktopTrayService>();
        builder.Services.AddSingleton<LinuxSpeechSynthesisService>();
        builder.Services.AddSingleton<IPlatformSpeechSynthesisService>(
            provider => provider.GetRequiredService<LinuxSpeechSynthesisService>());
        return builder.Build();
    }
}
