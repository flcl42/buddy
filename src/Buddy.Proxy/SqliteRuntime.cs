using System.Runtime.InteropServices;

namespace Buddy.Proxy;

internal static class SqliteRuntime
{
#if BUDDY_SYSTEM_SQLITE
    private static readonly object Sync = new();
    private static bool _initialized;
    private static IntPtr _libraryHandle;

    public static void Initialize()
    {
        lock (Sync)
        {
            if (_initialized)
            {
                return;
            }

            const string libraryName = "libsqlite3.so.0";
            _libraryHandle = NativeLibrary.Load(libraryName);
            NativeExportResolver resolver = new(_libraryHandle);
            SQLitePCL.SQLite3Provider_dynamic_cdecl.Setup(
                libraryName,
                resolver);
            SQLitePCL.raw.SetProvider(
                new SQLitePCL.SQLite3Provider_dynamic_cdecl());
            SQLitePCL.raw.FreezeProvider();
            _initialized = true;
        }
    }

    private sealed class NativeExportResolver(IntPtr libraryHandle)
        : SQLitePCL.IGetFunctionPointer
    {
        public IntPtr GetFunctionPointer(string name)
        {
            return NativeLibrary.TryGetExport(
                libraryHandle,
                name,
                out IntPtr address)
                ? address
                : IntPtr.Zero;
        }
    }
#else
    public static void Initialize()
    {
        // The desktop/test bundle configures its packaged SQLite build.
    }
#endif
}
