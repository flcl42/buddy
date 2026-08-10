using System.Globalization;
using System.Text;

namespace Buddy.App.WinUI;

internal static class StartupDiagnostics
{
    private static readonly object WriteLock = new();
    private static readonly string LogDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Buddy",
        "logs");
    private static readonly string LogPath = Path.Combine(LogDirectory, "startup.log");
    private static bool _initialized;

    internal static void Initialize()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        Write("diagnostics initialized");
    }

    internal static void Write(string message, Exception? exception = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        StringBuilder entry = new();
        entry.Append(DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture));
        entry.Append(" [");
        entry.Append(Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
        entry.Append("] ");
        entry.AppendLine(message);

        if (exception is not null)
        {
            entry.AppendLine(exception.ToString());
            entry.Append("HRESULT: 0x");
            entry.AppendLine(exception.HResult.ToString("X8", CultureInfo.InvariantCulture));
        }

        try
        {
            lock (WriteLock)
            {
                Directory.CreateDirectory(LogDirectory);
                File.AppendAllText(LogPath, entry.ToString());
            }
        }
        catch (Exception writeException) when (
            writeException is IOException
            or UnauthorizedAccessException
            or System.Security.SecurityException)
        {
            System.Diagnostics.Debug.WriteLine(entry.ToString());
        }
    }

    private static void OnAppDomainUnhandledException(
        object sender,
        UnhandledExceptionEventArgs eventArgs)
    {
        Exception? exception = eventArgs.ExceptionObject as Exception;
        Write(
            $"AppDomain unhandled exception; terminating={eventArgs.IsTerminating}",
            exception);
    }

    private static void OnUnobservedTaskException(
        object? sender,
        UnobservedTaskExceptionEventArgs eventArgs)
    {
        Write("unobserved task exception", eventArgs.Exception);
    }
}
