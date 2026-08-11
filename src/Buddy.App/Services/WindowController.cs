using H.NotifyIcon;

namespace Buddy.App.Services;

public sealed class WindowController : IWindowController
{
    private int _exitStarted;

    public void Show()
    {
        Window? window = GetWindow();
        if (window is null)
        {
            return;
        }

        H.NotifyIcon.WindowExtensions.Show(window);
        H.NotifyIcon.WindowExtensions.Activate(window);
    }

    public void Hide()
    {
        Window? window = GetWindow();
        if (window is not null)
        {
            H.NotifyIcon.WindowExtensions.Hide(window);
        }
    }

    public void ExitApplication()
    {
        if (Interlocked.Exchange(ref _exitStarted, 1) != 0)
        {
            return;
        }

        Window? window = GetWindow();
        if (window is BuddyWindow buddyWindow)
        {
            buddyWindow.AllowClose();
        }

        // Native transcription or model work can take a long time to observe
        // cancellation. The capture journal and SQLite transactions are
        // recoverable, so an explicit Exit must never be held hostage by that
        // background work. Normal shutdown gets a short grace period; this
        // watchdog is only reached if the process is still alive afterwards.
        _ = Task.Run(
            async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
                Environment.Exit(0);
            });

        // Quit is intentionally called only after the watchdog is armed. Even
        // if a framework shutdown callback blocks synchronously, explicit Exit
        // still completes within the grace period.
        Application.Current?.Quit();
    }

    private static Window? GetWindow()
    {
        IReadOnlyList<Window>? windows = Application.Current?.Windows;
        return windows is { Count: > 0 } ? windows[0] : null;
    }
}
