using Buddy.App.Controls;
using Buddy.App.Services;
using Buddy.App.WinUI;
using Microsoft.Extensions.Logging;

#if WINDOWS
using Buddy.App.Platforms.Windows;
using H.NotifyIcon;
#endif

namespace Buddy.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        StartupDiagnostics.Initialize();
        StartupDiagnostics.Write("MauiProgram.CreateMauiApp building services");
        MauiAppBuilder builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();

#if WINDOWS
        builder.UseNotifyIcon();
        FramedInputChrome.Register();
        builder.ConfigureMauiHandlers(
            handlers => handlers.AddHandler<
                MarkdownMessageView,
                MarkdownMessageViewHandler>());
#endif

#if DEBUG
        builder.Logging.AddDebug();
#endif

        BuddyServiceRegistration.AddBuddyServices(builder.Services);
        MauiApp app = builder.Build();
        StartupDiagnostics.Write("MauiProgram.CreateMauiApp complete");
        return app;
    }
}
