using System.Reflection;

namespace Buddy.App.Services;

public sealed class PortableWindowController : IWindowController
{
    private int _exitStarted;

    public void Show()
    {
        Window? window = GetWindow();
        if (window is null)
        {
            return;
        }

        window.Dispatcher.Dispatch(
            () => SetNativeVisibility(window.Handler?.PlatformView, visible: true));
    }

    public void Hide()
    {
        Window? window = GetWindow();
        if (window is not null)
        {
            window.Dispatcher.Dispatch(
                () => SetNativeVisibility(window.Handler?.PlatformView, visible: false));
        }
    }

    public void ExitApplication()
    {
        if (Interlocked.Exchange(ref _exitStarted, 1) != 0)
        {
            return;
        }

        _ = Task.Run(
            async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
                Environment.Exit(0);
            });
        Application.Current?.Quit();
    }

    private static Window? GetWindow()
    {
        IReadOnlyList<Window>? windows = Application.Current?.Windows;
        return windows is { Count: > 0 } ? windows[0] : null;
    }

    private static void SetNativeVisibility(object? nativeWindow, bool visible)
    {
        if (nativeWindow is null)
        {
            return;
        }

        Type type = nativeWindow.GetType();
        PropertyInfo? hidden = type.GetProperty("Hidden");
        if (hidden?.CanWrite == true && hidden.PropertyType == typeof(bool))
        {
            hidden.SetValue(nativeWindow, !visible);
        }

        string[] candidateMethods = visible
            ? ["Show", "Present", "Activate", "MakeKeyAndVisible"]
            : ["Hide"];
        foreach (string methodName in candidateMethods)
        {
            MethodInfo? method = type.GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.Public,
                binder: null,
                types: Type.EmptyTypes,
                modifiers: null);
            if (method is not null)
            {
                method.Invoke(nativeWindow, null);
                break;
            }
        }
    }
}
