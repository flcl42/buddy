[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [int] $RdpProcessId,

    [Parameter(Mandatory)]
    [int] $GuestX,

    [Parameter(Mandatory)]
    [int] $GuestY
)

$ErrorActionPreference = 'Stop'
Add-Type @'
using System;
using System.Text;
using System.Runtime.InteropServices;

public static class SafePhysicalRdpClick
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    public struct LASTINPUTINFO { public uint cbSize, dwTime; }

    public delegate bool EnumWindowsProc(IntPtr window, IntPtr data);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr window, out RECT rect);
    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(IntPtr window, IntPtr after,
        int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")]
    public static extern bool EnumChildWindows(IntPtr parent,
        EnumWindowsProc callback, IntPtr data);
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern int GetClassName(IntPtr window,
        StringBuilder name, int maximum);
    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll")]
    public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")]
    public static extern void mouse_event(uint flags, uint x, uint y,
        uint data, UIntPtr extraInfo);
    [DllImport("user32.dll")]
    public static extern bool GetLastInputInfo(ref LASTINPUTINFO input);
    [DllImport("user32.dll")]
    public static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")]
    public static extern IntPtr WindowFromPoint(POINT point);
    [DllImport("user32.dll")]
    public static extern void keybd_event(byte key, byte scanCode,
        uint flags, UIntPtr extraInfo);

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
}
'@

$deadline = (Get-Date).AddSeconds(45)
while ([SafePhysicalRdpClick]::IdleSeconds() -lt 3 -and
    (Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 250
}
if ([SafePhysicalRdpClick]::IdleSeconds() -lt 3) {
    throw 'The host did not become idle enough for the VM pointer action.'
}

$rdp = (Get-Process -Id $RdpProcessId -ErrorAction Stop).MainWindowHandle
$inputWindow = [SafePhysicalRdpClick]::FindInputWindow($rdp)
if ($inputWindow -eq [IntPtr]::Zero) {
    throw 'The RDP input window was not found.'
}

$previousWindow = [SafePhysicalRdpClick]::GetForegroundWindow()
$previousCursor = New-Object SafePhysicalRdpClick+POINT
[void] [SafePhysicalRdpClick]::GetCursorPos([ref] $previousCursor)
$rdpRect = New-Object SafePhysicalRdpClick+RECT
$inputRect = New-Object SafePhysicalRdpClick+RECT
[void] [SafePhysicalRdpClick]::GetWindowRect($rdp, [ref] $rdpRect)
[void] [SafePhysicalRdpClick]::GetWindowRect($inputWindow, [ref] $inputRect)
$width = $rdpRect.Right - $rdpRect.Left
$height = $rdpRect.Bottom - $rdpRect.Top
$inputOffsetX = $inputRect.Left - $rdpRect.Left
$inputOffsetY = $inputRect.Top - $rdpRect.Top
$virtualLeft = [SafePhysicalRdpClick]::GetSystemMetrics(76)
$virtualTop = [SafePhysicalRdpClick]::GetSystemMetrics(77)
$virtualWidth = [SafePhysicalRdpClick]::GetSystemMetrics(78)
$targetX = $virtualLeft + $virtualWidth - 20
$targetY = $virtualTop + 20
$temporaryX = $targetX - $inputOffsetX - $GuestX
$temporaryY = $targetY - $inputOffsetY - $GuestY

try {
    [void] [SafePhysicalRdpClick]::SetWindowPos(
        $rdp, [IntPtr]::Zero, $temporaryX, $temporaryY,
        $width, $height, 0x0014)
    [SafePhysicalRdpClick]::AltPulse()
    $foregroundSet = [SafePhysicalRdpClick]::SetForegroundWindow($rdp)
    Start-Sleep -Milliseconds 500
    $movedRdpRect = New-Object SafePhysicalRdpClick+RECT
    $movedInputRect = New-Object SafePhysicalRdpClick+RECT
    [void] [SafePhysicalRdpClick]::GetWindowRect($rdp, [ref] $movedRdpRect)
    [void] [SafePhysicalRdpClick]::GetWindowRect(
        $inputWindow, [ref] $movedInputRect)
    $cursorSet = [SafePhysicalRdpClick]::SetCursorPos($targetX, $targetY)
    Start-Sleep -Milliseconds 350
    $targetPoint = New-Object SafePhysicalRdpClick+POINT
    $targetPoint.X = $targetX
    $targetPoint.Y = $targetY
    $windowAtTarget = [SafePhysicalRdpClick]::WindowFromPoint($targetPoint)
    $windowAtTargetClass = [Text.StringBuilder]::new(128)
    [void] [SafePhysicalRdpClick]::GetClassName(
        $windowAtTarget, $windowAtTargetClass, $windowAtTargetClass.Capacity)
    [SafePhysicalRdpClick]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)
    [SafePhysicalRdpClick]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 1800
}
finally {
    [void] [SafePhysicalRdpClick]::SetWindowPos(
        $rdp, [IntPtr]::Zero, $rdpRect.Left, $rdpRect.Top,
        $width, $height, 0x0014)
    [void] [SafePhysicalRdpClick]::SetCursorPos(
        $previousCursor.X, $previousCursor.Y)
    if ($previousWindow -ne [IntPtr]::Zero) {
        [SafePhysicalRdpClick]::AltPulse()
        [void] [SafePhysicalRdpClick]::SetForegroundWindow($previousWindow)
    }
}

[pscustomobject]@{
    ForegroundSet = $foregroundSet
    CursorSet = $cursorSet
    RequestedGuestX = $GuestX
    RequestedGuestY = $GuestY
    TargetScreenX = $targetX
    TargetScreenY = $targetY
    ActualGuestX = $targetX - $movedInputRect.Left
    ActualGuestY = $targetY - $movedInputRect.Top
    MovedRdpLeft = $movedRdpRect.Left
    MovedRdpTop = $movedRdpRect.Top
    MovedInputLeft = $movedInputRect.Left
    MovedInputTop = $movedInputRect.Top
    WindowAtTarget = $windowAtTarget
    WindowAtTargetClass = $windowAtTargetClass.ToString()
}
