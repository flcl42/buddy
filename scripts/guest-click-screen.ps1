[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [int] $X,

    [Parameter(Mandatory)]
    [int] $Y,

    [switch] $MoveOnly,

    [switch] $KeepCursor
)

$ErrorActionPreference = 'Stop'
Add-Type @'
using System;
using System.Text;
using System.Runtime.InteropServices;

public static class GuestPointer
{
    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X, Y; }

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll")]
    public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")]
    public static extern void mouse_event(uint flags, uint x, uint y,
        uint data, UIntPtr extraInfo);
    [DllImport("user32.dll")]
    public static extern void keybd_event(byte key, byte scanCode,
        uint flags, UIntPtr extraInfo);
    [DllImport("user32.dll")]
    public static extern IntPtr WindowFromPoint(POINT point);
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern int GetClassName(IntPtr window,
        StringBuilder name, int maximum);

    public static void AltPulse()
    {
        keybd_event(0x12, 0, 0, UIntPtr.Zero);
        keybd_event(0x12, 0, 2, UIntPtr.Zero);
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

$previousCursor = New-Object GuestPointer+POINT
[void] [GuestPointer]::GetCursorPos([ref] $previousCursor)
[GuestPointer]::AltPulse()
$foregroundSet = [GuestPointer]::SetForegroundWindow($process.MainWindowHandle)
Start-Sleep -Milliseconds 500
$cursorSet = [GuestPointer]::SetCursorPos($X, $Y)
Start-Sleep -Milliseconds 250
$targetPoint = New-Object GuestPointer+POINT
$targetPoint.X = $X
$targetPoint.Y = $Y
$windowAtPoint = [GuestPointer]::WindowFromPoint($targetPoint)
$windowClass = [Text.StringBuilder]::new(128)
[void] [GuestPointer]::GetClassName(
    $windowAtPoint, $windowClass, $windowClass.Capacity)
if (-not $MoveOnly) {
    [GuestPointer]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)
    [GuestPointer]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)
}
Start-Sleep -Milliseconds 750
if (-not $KeepCursor) {
    [void] [GuestPointer]::SetCursorPos($previousCursor.X, $previousCursor.Y)
}

[pscustomobject]@{
    ProcessId = $process.Id
    ForegroundSet = $foregroundSet
    CursorSet = $cursorSet
    X = $X
    Y = $Y
    MoveOnly = [bool] $MoveOnly
    KeepCursor = [bool] $KeepCursor
    WindowAtPoint = $windowAtPoint.ToInt64()
    WindowClass = $windowClass.ToString()
} | ConvertTo-Json
