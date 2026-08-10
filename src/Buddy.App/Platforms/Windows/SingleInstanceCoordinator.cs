namespace Buddy.App.WinUI;

public static class SingleInstanceCoordinator
{
    public static event EventHandler? ActivationRequested;

    internal static void RaiseActivationRequested()
    {
        ActivationRequested?.Invoke(null, EventArgs.Empty);
    }
}
