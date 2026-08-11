using Buddy.App.WinUI;
using H.NotifyIcon;
using Microsoft.Maui;
using Microsoft.UI.Windowing;
using Windows.Graphics;

namespace Buddy.App;

public sealed class BuddyWindow : Window
{
    private AppWindow? _appWindow;
    private bool _allowClose;
    private bool _hasPositionedInitialWindow;

    public BuddyWindow(Page page)
        : base(page)
    {
        StartupDiagnostics.Write("BuddyWindow constructor starting");
        Title = "Chitchat Buddy";
        Width = 1_260;
        Height = 830;
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
            AppWindow initialWindow = _appWindow;
            Dispatcher.Dispatch(() => PositionInitialWindow(initialWindow));
        }
    }

    private void PositionInitialWindow(AppWindow appWindow)
    {
        if (_hasPositionedInitialWindow || _appWindow != appWindow)
        {
            return;
        }

        DisplayArea displayArea = DisplayArea.GetFromWindowId(
            appWindow.Id,
            DisplayAreaFallback.Primary);
        RectInt32 workArea = displayArea.WorkArea;
        SizeInt32 windowSize = appWindow.Size;
        (int x, int y) = WindowPlacement.CenterWithin(
            workArea.X,
            workArea.Y,
            workArea.Width,
            workArea.Height,
            windowSize.Width,
            windowSize.Height);

        appWindow.Move(new PointInt32(x, y));
        _hasPositionedInitialWindow = true;
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
