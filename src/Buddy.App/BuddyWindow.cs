using Buddy.App.WinUI;
using H.NotifyIcon;
using Microsoft.Maui;
using Microsoft.UI.Windowing;

namespace Buddy.App;

public sealed class BuddyWindow : Window
{
    private AppWindow? _appWindow;
    private bool _allowClose;

    public BuddyWindow(Page page)
        : base(page)
    {
        StartupDiagnostics.Write("BuddyWindow constructor starting");
        Title = "Buddy";
        Width = 1_180;
        Height = 780;
        MinimumWidth = 900;
        MinimumHeight = 640;
        StartupDiagnostics.Write("BuddyWindow constructor complete");
    }

    public void AllowClose()
    {
        _allowClose = true;
    }

    protected override void OnHandlerChanged()
    {
        StartupDiagnostics.Write(
            $"BuddyWindow OnHandlerChanged; has-handler={Handler is not null}");
        if (_appWindow is not null)
        {
            _appWindow.Closing -= OnAppWindowClosing;
            _appWindow = null;
        }

        base.OnHandlerChanged();

        if (Handler?.PlatformView is MauiWinUIWindow nativeWindow)
        {
            _appWindow = nativeWindow.AppWindow;
            _appWindow.Closing += OnAppWindowClosing;
        }
    }

    private void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_allowClose)
        {
            return;
        }

        args.Cancel = true;
        H.NotifyIcon.WindowExtensions.Hide(this);
    }
}
