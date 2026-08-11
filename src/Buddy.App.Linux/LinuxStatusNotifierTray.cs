using Buddy.App.State;
using Microsoft.Extensions.Logging;
using Tmds.DBus.Protocol;

namespace Buddy.App.LinuxTray;

internal sealed class LinuxStatusNotifierTray : IDisposable
{
    private const string WatcherService = "org.kde.StatusNotifierWatcher";
    private const string WatcherPath = "/StatusNotifierWatcher";
    private const string ItemPath = "/StatusNotifierItem";

    private static readonly Action<ILogger, Exception?> LogRegistrationFailed =
        LoggerMessage.Define(
            LogLevel.Warning,
            new EventId(1, "TrayRegistrationFailed"),
            "The StatusNotifier watcher rejected Buddy's tray registration.");

    private static readonly Action<ILogger, Exception?> LogWatcherStopped =
        LoggerMessage.Define(
            LogLevel.Warning,
            new EventId(2, "TrayWatcherStopped"),
            "Buddy stopped watching for a desktop StatusNotifier service.");

    private readonly ILogger _logger;
    private readonly Action _activate;
    private readonly CancellationTokenSource _lifetime = new();
    private DBusConnection? _connection;
    private NameOwnerWatcher? _watcher;
    private StatusNotifierItemHandler? _handler;
    private Task? _watchTask;
    private bool _disposed;

    public LinuxStatusNotifierTray(ILogger logger, Action activate)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _activate = activate ?? throw new ArgumentNullException(nameof(activate));
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        string address = DBusAddress.Session
            ?? throw new InvalidOperationException(
                "A D-Bus session address is not available.");
        var connection = new DBusConnection(address);
        try
        {
            await connection.ConnectAsync().ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            var handler = new StatusNotifierItemHandler(
                connection,
                ItemPath,
                _activate);
            connection.AddMethodHandler(handler);
            NameOwnerWatcher watcher = await connection
                .WatchNameOwnerAsync(WatcherService)
                .ConfigureAwait(false);

            _connection = connection;
            _handler = handler;
            _watcher = watcher;

            string? owner = watcher.GetCurrentOwner();
            if (owner is not null)
            {
                await TryRegisterAsync(connection).ConfigureAwait(false);
            }

            _watchTask = WatchAsync(connection, watcher, owner, _lifetime.Token);
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    public void Update(BuddyRuntimeMode mode, string toolTip)
    {
        StatusNotifierItemHandler? handler = _handler;
        if (handler is null || _disposed)
        {
            return;
        }

        handler.Update(mode, toolTip);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _lifetime.Cancel();

        DBusConnection? connection = _connection;
        _connection = null;
        if (connection is not null)
        {
            connection.RemoveMethodHandler(ItemPath);
        }

        _watcher?.Dispose();
        _watcher = null;
        connection?.Dispose();
        _handler = null;
        _watchTask = null;
        _lifetime.Dispose();
    }

    private async Task WatchAsync(
        DBusConnection connection,
        NameOwnerWatcher watcher,
        string? owner,
        CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (owner is null)
                {
                    owner = await watcher
                        .WaitForOwnerAsync(cancellationToken)
                        .ConfigureAwait(false);
                    await TryRegisterAsync(connection).ConfigureAwait(false);
                }

                CancellationToken ownerChanged =
                    watcher.GetOwnerChangedCancellationToken(owner);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    ownerChanged);
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, linked.Token)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (
                    !cancellationToken.IsCancellationRequested)
                {
                    owner = watcher.GetCurrentOwner();
                    if (owner is not null)
                    {
                        await TryRegisterAsync(connection).ConfigureAwait(false);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException) when (_disposed)
        {
        }
        catch (Exception error)
        {
            LogWatcherStopped(_logger, error);
        }
    }

    private async Task TryRegisterAsync(DBusConnection connection)
    {
        try
        {
            var watcher = new StatusNotifierWatcher(
                connection,
                WatcherService,
                new ObjectPath(WatcherPath));
            await watcher
                .RegisterStatusNotifierItemAsync(connection.UniqueName!)
                .ConfigureAwait(false);
            _handler?.PublishCurrentState();
        }
        catch (Exception error)
        {
            LogRegistrationFailed(_logger, error);
        }
    }
}

internal sealed class StatusNotifierItemHandler :
    DBusHandler,
    IStatusNotifierItemHandler,
    IStatusNotifierItemProperties
{
    private static readonly ObjectPath NoMenu = new("/NO_DBUSMENU");
    private static readonly (int, int, byte[])[] EmptyPixmap = [];
    private static readonly Dictionary<BuddyRuntimeMode, (int, int, byte[])[]>
        Pixmaps = new Dictionary<BuddyRuntimeMode, (int, int, byte[])[]>
        {
            [BuddyRuntimeMode.Idle] = TrayPixmap.Create('b', 0x5B, 0x5C, 0xE2),
            [BuddyRuntimeMode.Recording] = TrayPixmap.Create('r', 0xDC, 0x35, 0x45),
            [BuddyRuntimeMode.Processing] = TrayPixmap.Create('b', 0x5B, 0x5C, 0xE2),
            [BuddyRuntimeMode.Attention] = TrayPixmap.Create('!', 0xD9, 0x77, 0x06),
        };

    private readonly Action _activate;
    private TrayState _state = CreateState(
        BuddyRuntimeMode.Idle,
        "Chitchat Buddy");

    public StatusNotifierItemHandler(
        DBusConnection connection,
        string path,
        Action activate)
        : base(connection, path, handlesChildPaths: false, handleOnCapturedContext: false)
    {
        _activate = activate;
    }

    public string Category => "ApplicationStatus";

    public string Id => "chitchat-buddy";

    public string Title => Volatile.Read(ref _state).ToolTip;

    public string Status => Volatile.Read(ref _state).Status;

    public int WindowId => 0;

    public string IconThemePath => string.Empty;

    public ObjectPath Menu => NoMenu;

    public bool ItemIsMenu => false;

    public string IconName => string.Empty;

    public (int, int, byte[])[] IconPixmap => Volatile.Read(ref _state).Pixmap;

    public string OverlayIconName => string.Empty;

    public (int, int, byte[])[] OverlayIconPixmap => EmptyPixmap;

    public string AttentionIconName => string.Empty;

    public (int, int, byte[])[] AttentionIconPixmap => EmptyPixmap;

    public string AttentionMovieName => string.Empty;

    public (string, (int, int, byte[])[], string, string) ToolTip
    {
        get
        {
            TrayState state = Volatile.Read(ref _state);
            return (string.Empty, state.Pixmap, "Chitchat Buddy", state.ToolTip);
        }
    }

    public ValueTask HandleGetPropertyAsync(
        IStatusNotifierItemHandler.GetPropertyContext context) =>
        context.Handle(this);

    public ValueTask HandleGetAllPropertiesAsync(
        IStatusNotifierItemHandler.GetAllPropertiesContext context) =>
        context.Handle(this);

    public ValueTask ContextMenuAsync(int x, int y)
    {
        _activate();
        return ValueTask.CompletedTask;
    }

    public ValueTask ActivateAsync(int x, int y)
    {
        _activate();
        return ValueTask.CompletedTask;
    }

    public ValueTask SecondaryActivateAsync(int x, int y)
    {
        _activate();
        return ValueTask.CompletedTask;
    }

    public ValueTask ScrollAsync(int delta, string orientation) =>
        ValueTask.CompletedTask;

    public void Update(BuddyRuntimeMode mode, string toolTip)
    {
        Volatile.Write(ref _state, CreateState(mode, toolTip));
        PublishCurrentState();
    }

    public void PublishCurrentState()
    {
        Connection.EmitNewTitle(Path);
        Connection.EmitNewIcon(Path);
        Connection.EmitNewToolTip(Path);
        Connection.EmitNewStatus(Path, Status);
    }

    private static TrayState CreateState(BuddyRuntimeMode mode, string toolTip)
    {
        BuddyRuntimeMode iconMode = Pixmaps.ContainsKey(mode)
            ? mode
            : BuddyRuntimeMode.Idle;
        string status = mode == BuddyRuntimeMode.Attention
            ? "NeedsAttention"
            : "Active";
        return new TrayState(
            Pixmaps[iconMode],
            status,
            string.IsNullOrWhiteSpace(toolTip) ? "Chitchat Buddy" : toolTip);
    }

    private sealed record TrayState(
        (int, int, byte[])[] Pixmap,
        string Status,
        string ToolTip);
}

internal static class TrayPixmap
{
    private static readonly Dictionary<char, string[]> Glyphs =
        new Dictionary<char, string[]>
        {
            ['b'] = [
                "10000",
                "10000",
                "11110",
                "10001",
                "10001",
                "10001",
                "11110",
            ],
            ['r'] = [
                "00000",
                "00000",
                "10110",
                "11001",
                "10000",
                "10000",
                "10000",
            ],
            ['!'] = [
                "00100",
                "00100",
                "00100",
                "00100",
                "00100",
                "00000",
                "00100",
            ],
        };

    public static (int, int, byte[])[] Create(
        char glyph,
        byte red,
        byte green,
        byte blue) =>
        [
            (32, 32, Render(32, glyph, red, green, blue)),
            (64, 64, Render(64, glyph, red, green, blue)),
        ];

    private static byte[] Render(
        int size,
        char glyph,
        byte red,
        byte green,
        byte blue)
    {
        var pixels = new byte[size * size * 4];
        double center = (size - 1) / 2d;
        double radius = size * 0.45d;
        double radiusSquared = radius * radius;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                double dx = x - center;
                double dy = y - center;
                if ((dx * dx) + (dy * dy) <= radiusSquared)
                {
                    SetPixel(pixels, size, x, y, 0xFF, red, green, blue);
                }
            }
        }

        string[] pattern = Glyphs[glyph];
        int scale = Math.Max(2, size / 10);
        int glyphWidth = pattern[0].Length * scale;
        int glyphHeight = pattern.Length * scale;
        int left = (size - glyphWidth) / 2;
        int top = (size - glyphHeight) / 2;
        for (int row = 0; row < pattern.Length; row++)
        {
            for (int column = 0; column < pattern[row].Length; column++)
            {
                if (pattern[row][column] != '1')
                {
                    continue;
                }

                for (int y = 0; y < scale; y++)
                {
                    for (int x = 0; x < scale; x++)
                    {
                        SetPixel(
                            pixels,
                            size,
                            left + (column * scale) + x,
                            top + (row * scale) + y,
                            0xFF,
                            0xFF,
                            0xFF,
                            0xFF);
                    }
                }
            }
        }

        return pixels;
    }

    private static void SetPixel(
        byte[] pixels,
        int width,
        int x,
        int y,
        byte alpha,
        byte red,
        byte green,
        byte blue)
    {
        int offset = ((y * width) + x) * 4;
        pixels[offset] = alpha;
        pixels[offset + 1] = red;
        pixels[offset + 2] = green;
        pixels[offset + 3] = blue;
    }
}
