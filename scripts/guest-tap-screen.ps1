[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [int] $X,

    [Parameter(Mandatory)]
    [int] $Y
)

$ErrorActionPreference = 'Stop'
Add-Type @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

public static class GuestTouch
{
    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINTER_INFO
    {
        public uint pointerType;
        public uint pointerId;
        public uint frameId;
        public uint pointerFlags;
        public IntPtr sourceDevice;
        public IntPtr targetWindow;
        public POINT pixelLocation;
        public POINT himetricLocation;
        public POINT pixelLocationRaw;
        public POINT himetricLocationRaw;
        public uint time;
        public uint historyCount;
        public int inputData;
        public uint keyStates;
        public ulong performanceCount;
        public uint buttonChangeType;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINTER_TOUCH_INFO
    {
        public POINTER_INFO pointerInfo;
        public uint touchFlags;
        public uint touchMask;
        public RECT contact;
        public RECT contactRaw;
        public uint orientation;
        public uint pressure;
    }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool InitializeTouchInjection(
        uint maximumCount, uint feedbackMode);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool InjectTouchInput(
        uint count, [In] POINTER_TOUCH_INFO[] contacts);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    public static extern void keybd_event(byte key, byte scanCode,
        uint flags, UIntPtr extraInfo);

    public static void AltPulse()
    {
        keybd_event(0x12, 0, 0, UIntPtr.Zero);
        keybd_event(0x12, 0, 2, UIntPtr.Zero);
    }

    public static void Tap(int x, int y)
    {
        if (!InitializeTouchInjection(1, 1))
        {
            int error = Marshal.GetLastWin32Error();
            throw new Win32Exception(error, "InitializeTouchInjection failed: " + error);
        }

        POINTER_TOUCH_INFO contact = new POINTER_TOUCH_INFO();
        contact.pointerInfo.pointerType = 2;
        contact.pointerInfo.pointerId = 0;
        contact.pointerInfo.pixelLocation.X = x;
        contact.pointerInfo.pixelLocation.Y = y;
        contact.touchFlags = 0;
        contact.touchMask = 1;
        contact.contact.Left = x - 3;
        contact.contact.Top = y - 3;
        contact.contact.Right = x + 3;
        contact.contact.Bottom = y + 3;
        contact.orientation = 90;
        contact.pressure = 512;

        contact.pointerInfo.pointerFlags =
            0x00000002 | 0x00000004 | 0x00010000;
        POINTER_TOUCH_INFO[] contacts = new[] { contact };
        if (!InjectTouchInput(1, contacts))
        {
            int error = Marshal.GetLastWin32Error();
            throw new Win32Exception(error, "Touch down failed: " + error);
        }

        System.Threading.Thread.Sleep(80);
        contact.pointerInfo.pointerFlags =
            0x00000002 | 0x00000004 | 0x00020000;
        contacts[0] = contact;
        if (!InjectTouchInput(1, contacts))
        {
            int error = Marshal.GetLastWin32Error();
            throw new Win32Exception(error, "Touch update failed: " + error);
        }

        System.Threading.Thread.Sleep(80);
        contact.pointerInfo.pointerFlags =
            0x00000002 | 0x00040000;
        contacts[0] = contact;
        if (!InjectTouchInput(1, contacts))
        {
            int error = Marshal.GetLastWin32Error();
            throw new Win32Exception(error, "Touch up failed: " + error);
        }
    }
}
'@

$process = Get-Process -Name Buddy -ErrorAction Stop |
    Where-Object { $_.MainWindowHandle -ne [IntPtr]::Zero } |
    Sort-Object StartTime -Descending |
    Select-Object -First 1
if ($null -eq $process) {
    throw 'Buddy has no interactive window.'
}

[GuestTouch]::AltPulse()
$foregroundSet = [GuestTouch]::SetForegroundWindow($process.MainWindowHandle)
Start-Sleep -Milliseconds 400
[GuestTouch]::Tap($X, $Y)
Start-Sleep -Milliseconds 900

[pscustomobject]@{
    ProcessId = $process.Id
    ForegroundSet = $foregroundSet
    X = $X
    Y = $Y
} | ConvertTo-Json
