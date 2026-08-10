using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace Buddy.App.WinUI;

public partial class App : MauiWinUIApplication
{
    private AppInstance? _mainInstance;
    private DispatcherQueue? _dispatcherQueue;

    public App()
    {
        StartupDiagnostics.Initialize();
        UnhandledException += OnWinUiUnhandledException;
        StartupDiagnostics.Write("WinUI App constructor before InitializeComponent");
        InitializeComponent();
        StartupDiagnostics.Write("WinUI App constructor complete");
    }

    protected override MauiApp CreateMauiApp()
    {
        StartupDiagnostics.Write("CreateMauiApp starting");
        return MauiProgram.CreateMauiApp();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        StartupDiagnostics.Write("OnLaunched starting");
        AppActivationArguments activationArguments =
            AppInstance.GetCurrent().GetActivatedEventArgs();
        AppInstance mainInstance = AppInstance.FindOrRegisterForKey("com.flcl.buddy.main");
        StartupDiagnostics.Write(
            $"AppInstance acquired; is-current={mainInstance.IsCurrent}");

        if (!mainInstance.IsCurrent)
        {
            StartupDiagnostics.Write("redirecting activation to primary instance");
            await mainInstance.RedirectActivationToAsync(activationArguments);
            StartupDiagnostics.Write("activation redirected; exiting secondary instance");
            Environment.Exit(0);
            return;
        }

        _mainInstance = mainInstance;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        _mainInstance.Activated += OnInstanceActivated;
        StartupDiagnostics.Write("calling MAUI base OnLaunched");
        base.OnLaunched(args);
        StartupDiagnostics.Write("MAUI base OnLaunched returned");
    }

    private void OnInstanceActivated(object? sender, AppActivationArguments args)
    {
        _dispatcherQueue?.TryEnqueue(SingleInstanceCoordinator.RaiseActivationRequested);
    }

    private void OnWinUiUnhandledException(
        object sender,
        Microsoft.UI.Xaml.UnhandledExceptionEventArgs eventArgs)
    {
        StartupDiagnostics.Write(
            $"WinUI unhandled exception: {eventArgs.Message}",
            eventArgs.Exception);
    }
}
