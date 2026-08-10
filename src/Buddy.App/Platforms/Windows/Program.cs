using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;

namespace Buddy.App.WinUI;

public static class Program
{
    private const string RuntimeBaseVariable =
        "MICROSOFT_WINDOWSAPPRUNTIME_BASE_DIRECTORY";
    private const string BootstrapMarkerVariable =
        "BUDDY_SINGLE_FILE_BOOTSTRAPPED";

    [STAThread]
    public static void Main(string[] args)
    {
        StartupDiagnostics.Initialize();

        string runtimeDirectory = FindWindowsAppRuntimeDirectory();
        string runtimeBase = EnsureTrailingSeparator(runtimeDirectory);
        if (RequiresBootstrapRelaunch(runtimeDirectory))
        {
            RelaunchWithRuntimeBase(args, runtimeBase);
            return;
        }

        Environment.SetEnvironmentVariable(
            RuntimeBaseVariable,
            runtimeBase);
        StartupDiagnostics.Write(
            $"Windows App SDK runtime directory: {runtimeDirectory}");

        Marshal.ThrowExceptionForHR(WindowsAppRuntime_EnsureIsLoaded());
        WinRT.ComWrappersSupport.InitializeComWrappers();
        Microsoft.UI.Xaml.Application.Start(
            initializationParameters =>
            {
                _ = initializationParameters;
                DispatcherQueueSynchronizationContext context = new(
                    DispatcherQueue.GetForCurrentThread());
                SynchronizationContext.SetSynchronizationContext(context);
                _ = new App();
            });
    }

    private static string FindWindowsAppRuntimeDirectory()
    {
        foreach (string? candidate in GetRuntimeCandidates())
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            string fullPath = Path.GetFullPath(candidate);
            if (File.Exists(Path.Combine(fullPath, "Microsoft.ui.xaml.dll"))
                && File.Exists(
                    Path.Combine(fullPath, "Microsoft.WindowsAppRuntime.dll"))
                && File.Exists(Path.Combine(fullPath, "resources.pri")))
            {
                return fullPath;
            }
        }

        throw new DirectoryNotFoundException(
            "Buddy's extracted Windows App SDK runtime is missing.");
    }

    private static IEnumerable<string?> GetRuntimeCandidates()
    {
        yield return AppContext.BaseDirectory;

        if (AppContext.GetData("NATIVE_DLL_SEARCH_DIRECTORIES") is not string
            nativeSearchDirectories)
        {
            yield break;
        }

        foreach (string path in nativeSearchDirectories.Split(
                     Path.PathSeparator,
                     StringSplitOptions.RemoveEmptyEntries
                         | StringSplitOptions.TrimEntries))
        {
            yield return path;
        }
    }

    private static string EnsureTrailingSeparator(string path)
    {
        return Path.EndsInDirectorySeparator(path)
            ? path
            : path + Path.DirectorySeparatorChar;
    }

    private static bool RequiresBootstrapRelaunch(string runtimeDirectory)
    {
        string? executableDirectory = Path.GetDirectoryName(
            Environment.ProcessPath);
        if (string.Equals(
                executableDirectory,
                runtimeDirectory,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        bool alreadyBootstrapped = string.Equals(
            Environment.GetEnvironmentVariable(BootstrapMarkerVariable),
            "1",
            StringComparison.Ordinal);
        if (alreadyBootstrapped)
        {
            string? inheritedRuntimeBase =
                Environment.GetEnvironmentVariable(RuntimeBaseVariable);
            if (!string.Equals(
                    EnsureTrailingSeparator(inheritedRuntimeBase ?? string.Empty),
                    EnsureTrailingSeparator(runtimeDirectory),
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Buddy inherited an invalid Windows App SDK runtime path.");
            }

            return false;
        }

        return true;
    }

    private static void RelaunchWithRuntimeBase(
        IEnumerable<string> args,
        string runtimeBase)
    {
        string executablePath = Environment.ProcessPath
            ?? throw new InvalidOperationException(
                "Buddy could not resolve its executable path.");
        ProcessStartInfo startInfo = new(executablePath)
        {
            UseShellExecute = false,
            WorkingDirectory = Environment.CurrentDirectory,
        };
        foreach (string argument in args)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment[RuntimeBaseVariable] = runtimeBase;
        startInfo.Environment[BootstrapMarkerVariable] = "1";
        StartupDiagnostics.Write(
            "Restarting once with the extracted Windows App SDK runtime active.");
        _ = Process.Start(startInfo)
            ?? throw new InvalidOperationException(
                "Buddy could not start its initialized process.");
    }

    [DllImport(
        "Microsoft.WindowsAppRuntime.dll",
        CharSet = CharSet.Unicode,
        ExactSpelling = true)]
    private static extern int WindowsAppRuntime_EnsureIsLoaded();
}
