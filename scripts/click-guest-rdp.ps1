[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [int] $RdpProcessId,

    [Parameter(Mandatory)]
    [int] $GuestX,

    [Parameter(Mandatory)]
    [int] $GuestY,

    [switch] $Right
)

$ErrorActionPreference = 'Stop'
Add-Type @'
using System;
using System.Text;
using System.Runtime.InteropServices;

public static class SafeRdpClick
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    public delegate bool EnumWindowsProc(IntPtr window, IntPtr data);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr window, out RECT rect);

    [DllImport("user32.dll")]
    public static extern bool GetClientRect(IntPtr window, out RECT rect);

    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(
        IntPtr window,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll")]
    public static extern bool GetLastInputInfo(ref LASTINPUTINFO input);

    [DllImport("user32.dll")]
    public static extern bool EnumChildWindows(
        IntPtr parent,
        EnumWindowsProc callback,
        IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern int GetClassName(
        IntPtr window,
        StringBuilder className,
        int maximum);

    [DllImport("user32.dll")]
    public static extern bool PostMessage(
        IntPtr window,
        uint message,
        IntPtr wParam,
        IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern void keybd_event(
        byte virtualKey,
        byte scanCode,
        uint flags,
        UIntPtr extraInfo);

    public static double IdleSeconds()
    {
        LASTINPUTINFO input = new LASTINPUTINFO();
        input.cbSize = (uint)Marshal.SizeOf(input);
        GetLastInputInfo(ref input);
        uint elapsed = unchecked((uint)Environment.TickCount - input.dwTime);
        return elapsed / 1000.0;
    }

    public static void AltPulse()
    {
        keybd_event(0x12, 0, 0, UIntPtr.Zero);
        keybd_event(0x12, 0, 2, UIntPtr.Zero);
    }

    public static IntPtr FindInputWindow(IntPtr parent)
    {
        IntPtr result = IntPtr.Zero;
        EnumChildWindows(parent, delegate(IntPtr window, IntPtr data)
        {
            StringBuilder name = new StringBuilder(128);
            GetClassName(window, name, name.Capacity);
            if (name.ToString() == "IHWindowClass")
            {
                result = window;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return result;
    }

    public static IntPtr Point(int x, int y)
    {
        return (IntPtr)((y << 16) | (x & 0xffff));
    }
}
'@

$deadline = (Get-Date).AddSeconds(30)
while ([SafeRdpClick]::IdleSeconds() -lt 2 -and (Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 200
}
if ([SafeRdpClick]::IdleSeconds() -lt 2) {
    throw 'The host did not become idle enough for the guest click.'
}

$rdp = (Get-Process -Id $RdpProcessId -ErrorAction Stop).MainWindowHandle
$inputWindow = [SafeRdpClick]::FindInputWindow($rdp)
if ($inputWindow -eq [IntPtr]::Zero) {
    throw 'The RDP input window was not found.'
}
$previous = [SafeRdpClick]::GetForegroundWindow()
$rect = New-Object SafeRdpClick+RECT
[void] [SafeRdpClick]::GetWindowRect($rdp, [ref] $rect)
$width = $rect.Right - $rect.Left
$height = $rect.Bottom - $rect.Top

try {
    [void] [SafeRdpClick]::SetWindowPos(
        $rdp, [IntPtr]::Zero, -4000, 0, $width, $height, 0x0014)
    [SafeRdpClick]::AltPulse()
    $foregroundSet = [SafeRdpClick]::SetForegroundWindow($rdp)
    Start-Sleep -Milliseconds 600

    $point = [SafeRdpClick]::Point($GuestX, $GuestY)
    [void] [SafeRdpClick]::PostMessage(
        $inputWindow, 0x0200, [IntPtr]::Zero, $point)
    if ($Right) {
        [void] [SafeRdpClick]::PostMessage(
            $inputWindow, 0x0204, [IntPtr]2, $point)
        [void] [SafeRdpClick]::PostMessage(
            $inputWindow, 0x0205, [IntPtr]::Zero, $point)
    }
    else {
        [void] [SafeRdpClick]::PostMessage(
            $inputWindow, 0x0201, [IntPtr]1, $point)
        [void] [SafeRdpClick]::PostMessage(
            $inputWindow, 0x0202, [IntPtr]::Zero, $point)
    }
    Start-Sleep -Milliseconds 1200
}
finally {
    [void] [SafeRdpClick]::SetWindowPos(
        $rdp, [IntPtr]::Zero, $rect.Left, $rect.Top, $width, $height, 0x0014)
    if ($previous -ne [IntPtr]::Zero) {
        [SafeRdpClick]::AltPulse()
        [void] [SafeRdpClick]::SetForegroundWindow($previous)
    }
}

$clientRect = New-Object SafeRdpClick+RECT
[void] [SafeRdpClick]::GetClientRect($inputWindow, [ref] $clientRect)
[pscustomobject]@{
    ForegroundSet = $foregroundSet
    InputWindow = $inputWindow
    ClientWidth = $clientRect.Right - $clientRect.Left
    ClientHeight = $clientRect.Bottom - $clientRect.Top
    GuestX = $GuestX
    GuestY = $GuestY
}
