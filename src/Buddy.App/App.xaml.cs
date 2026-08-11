using Buddy.App.Services;
using Buddy.App.WinUI;

namespace Buddy.App;

public partial class App : Application
{
    private readonly MainPage _mainPage;
    private readonly IWindowController _window;

    public App(
        MainPage mainPage,
        IWindowController window,
        UiLocalizationService localization)
    {
        StartupDiagnostics.Write("MAUI App constructor starting");
        _mainPage = mainPage ?? throw new ArgumentNullException(nameof(mainPage));
        _window = window ?? throw new ArgumentNullException(nameof(window));
        InitializeComponent();
        (localization ?? throw new ArgumentNullException(nameof(localization)))
            .Apply("en");
        UserAppTheme = AppTheme.Light;
#if WINDOWS
        SingleInstanceCoordinator.ActivationRequested += OnActivationRequested;
#endif
        StartupDiagnostics.Write("MAUI App constructor complete");
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        StartupDiagnostics.Write("MAUI App creating BuddyWindow");
        return new BuddyWindow(_mainPage);
    }

#if WINDOWS
    private void OnActivationRequested(object? sender, EventArgs eventArgs)
    {
        MainThread.BeginInvokeOnMainThread(_window.Show);
    }
#endif
}
