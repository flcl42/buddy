using H.NotifyIcon;

namespace Buddy.App.Services;

public sealed class WindowController : IWindowController
{
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
        Window? window = GetWindow();
        if (window is BuddyWindow buddyWindow)
        {
            buddyWindow.AllowClose();
        }

        Application.Current?.Quit();
    }

    private static Window? GetWindow()
    {
        IReadOnlyList<Window>? windows = Application.Current?.Windows;
        return windows is { Count: > 0 } ? windows[0] : null;
    }
}
