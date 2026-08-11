using Buddy.App.LinuxTray;
using Buddy.App.Services;
using Buddy.App.State;
using Buddy.App.ViewModels;
using Microsoft.Extensions.Logging;

namespace Buddy.App;

public sealed class LinuxDesktopTrayService : IDesktopTrayService
{
    private static readonly Action<ILogger, Exception?> LogTrayUnavailable =
        LoggerMessage.Define(
            LogLevel.Warning,
            new EventId(1, "TrayUnavailable"),
            "The desktop does not currently expose a StatusNotifier watcher; Buddy will keep running without a tray icon.");

    private readonly ILogger<LinuxDesktopTrayService> _logger;
    private readonly IWindowController _window;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private LinuxStatusNotifierTray? _trayIcon;
    private BuddyRuntimeMode _mode;
    private string _toolTip = "Chitchat Buddy";
    private bool _disposed;

    public LinuxDesktopTrayService(
        ILoggerFactory loggerFactory,
        IWindowController window)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);
        _logger = loggerFactory.CreateLogger<LinuxDesktopTrayService>();
        _window = window ?? throw new ArgumentNullException(nameof(window));
    }

    public async Task InitializeAsync(
        MainViewModel viewModel,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_trayIcon is not null)
            {
                return;
            }

            LinuxStatusNotifierTray? pendingTrayIcon = null;
            try
            {
                pendingTrayIcon = new LinuxStatusNotifierTray(
                    _logger,
                    _window.Show);
                await pendingTrayIcon
                    .InitializeAsync(cancellationToken)
                    .ConfigureAwait(false);
                _trayIcon = pendingTrayIcon;
                pendingTrayIcon = null;
                ApplyState();
            }
            catch (Exception error) when (
                error is not OperationCanceledException)
            {
                LogTrayUnavailable(_logger, error);
            }
            finally
            {
                pendingTrayIcon?.Dispose();
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Update(BuddyRuntimeMode mode, string toolTip)
    {
        _mode = mode;
        _toolTip = string.IsNullOrWhiteSpace(toolTip)
            ? "Chitchat Buddy"
            : toolTip;
        ApplyState();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _trayIcon?.Dispose();
        _trayIcon = null;
        _gate.Dispose();
    }

    private void ApplyState()
    {
        LinuxStatusNotifierTray? trayIcon = _trayIcon;
        if (trayIcon is null)
        {
            return;
        }
        trayIcon.Update(_mode, _toolTip);
    }
}
