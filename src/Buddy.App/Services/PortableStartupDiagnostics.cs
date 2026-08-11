#if !WINDOWS
using System.Globalization;
using System.Text;

namespace Buddy.App.WinUI;

internal static class StartupDiagnostics
{
    private static readonly object WriteLock = new();
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Buddy",
        "logs",
        "startup.log");
    private static bool _initialized;

    internal static void Initialize()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Write("unhandled application exception", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
            Write("unobserved task exception", args.Exception);
        Write("diagnostics initialized");
    }

    internal static void Write(string message, Exception? exception = null)
    {
        try
        {
            lock (WriteLock)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                StringBuilder line = new()
                {
                    Capacity = 256,
                };
                line.Append(DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture));
                line.Append(" | ");
                line.Append(message);
                if (exception is not null)
                {
                    line.Append(" | ");
                    line.Append(exception);
                }

                line.AppendLine();
                File.AppendAllText(LogPath, line.ToString());
            }
        }
        catch
        {
            // Diagnostics must never make startup fail.
        }
    }
}
#endif
