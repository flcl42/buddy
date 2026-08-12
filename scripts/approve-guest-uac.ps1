[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [int] $RdpProcessId
)

$ErrorActionPreference = 'Stop'
Add-Type @'
using System;
using System.Runtime.InteropServices;

public static class SafeGuestConsent
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

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr window, out RECT rect);

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

    public static void Key(byte virtualKey, bool release)
    {
        keybd_event(virtualKey, 0, release ? 2u : 0u, UIntPtr.Zero);
    }
}
'@

$deadline = (Get-Date).AddSeconds(30)
while ([SafeGuestConsent]::IdleSeconds() -lt 2 -and
    (Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 200
}
if ([SafeGuestConsent]::IdleSeconds() -lt 2) {
    throw 'The host did not become idle enough to approve the guest prompt.'
}

$rdp = (Get-Process -Id $RdpProcessId -ErrorAction Stop).MainWindowHandle
$previousForeground = [SafeGuestConsent]::GetForegroundWindow()
$rect = New-Object SafeGuestConsent+RECT
[void] [SafeGuestConsent]::GetWindowRect($rdp, [ref] $rect)
$width = $rect.Right - $rect.Left
$height = $rect.Bottom - $rect.Top

try {
    [void] [SafeGuestConsent]::SetWindowPos(
        $rdp,
        [IntPtr]::Zero,
        -4000,
        0,
        $width,
        $height,
        0x0014)
    [SafeGuestConsent]::Key(0x12, $false)
    [SafeGuestConsent]::Key(0x12, $true)
    [void] [SafeGuestConsent]::SetForegroundWindow($rdp)
    Start-Sleep -Milliseconds 300

    # The UAC dialog opens with No focused. Move to Yes, then approve it.
    [SafeGuestConsent]::Key(0x25, $false)
    [SafeGuestConsent]::Key(0x25, $true)
    Start-Sleep -Milliseconds 120
    [SafeGuestConsent]::Key(0x0D, $false)
    [SafeGuestConsent]::Key(0x0D, $true)
    Start-Sleep -Milliseconds 350
}
finally {
    [void] [SafeGuestConsent]::SetWindowPos(
        $rdp,
        [IntPtr]::Zero,
        $rect.Left,
        $rect.Top,
        $width,
        $height,
        0x0014)
    if ($previousForeground -ne [IntPtr]::Zero) {
        [SafeGuestConsent]::Key(0x12, $false)
        [SafeGuestConsent]::Key(0x12, $true)
        [void] [SafeGuestConsent]::SetForegroundWindow($previousForeground)
    }
}
