using System.Windows.Input;

#if WINDOWS
using H.NotifyIcon;
#endif

namespace Buddy.App.Controls;

#if WINDOWS
public sealed class BuddyTrayIcon : TaskbarIcon
{
}
#else
/// <summary>
/// XAML placeholder for platforms whose tray icon is owned by a native
/// desktop service rather than by a MAUI visual element.
/// </summary>
public sealed class BuddyTrayIcon : ContentView
{
    public static readonly BindableProperty DoubleClickCommandProperty =
        BindableProperty.Create(
            nameof(DoubleClickCommand),
            typeof(ICommand),
            typeof(BuddyTrayIcon));

    public static readonly BindableProperty LeftClickCommandProperty =
        BindableProperty.Create(
            nameof(LeftClickCommand),
            typeof(ICommand),
            typeof(BuddyTrayIcon));

    public static readonly BindableProperty NoLeftClickDelayProperty =
        BindableProperty.Create(
            nameof(NoLeftClickDelay),
            typeof(bool),
            typeof(BuddyTrayIcon));

    public static readonly BindableProperty ToolTipTextProperty =
        BindableProperty.Create(
            nameof(ToolTipText),
            typeof(string),
            typeof(BuddyTrayIcon),
            string.Empty);

    public ICommand? DoubleClickCommand
    {
        get => (ICommand?)GetValue(DoubleClickCommandProperty);
        set => SetValue(DoubleClickCommandProperty, value);
    }

    public ICommand? LeftClickCommand
    {
        get => (ICommand?)GetValue(LeftClickCommandProperty);
        set => SetValue(LeftClickCommandProperty, value);
    }

    public bool NoLeftClickDelay
    {
        get => (bool)GetValue(NoLeftClickDelayProperty);
        set => SetValue(NoLeftClickDelayProperty, value);
    }

    public string ToolTipText
    {
        get => (string)GetValue(ToolTipTextProperty);
        set => SetValue(ToolTipTextProperty, value);
    }
}
#endif
