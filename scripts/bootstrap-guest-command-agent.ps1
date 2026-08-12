[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [int] $RdpProcessId,

    [string] $ReadyPath = '',

    [switch] $OpenRunFirst,

    [string] $Command = '',

    [switch] $SkipReadyWait
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($ReadyPath)) {
    $ReadyPath = Join-Path $PSScriptRoot '..\artifacts\vm\agent\ready.json'
}
if ([string]::IsNullOrWhiteSpace($Command)) {
    $agentPath = [System.IO.Path]::GetFullPath(
        (Join-Path $PSScriptRoot 'guest-command-agent.ps1'))
    $driveRoot = [System.IO.Path]::GetPathRoot($agentPath)
    if ([string]::IsNullOrWhiteSpace($driveRoot) -or
        $driveRoot.Length -lt 2 -or
        $driveRoot[1] -ne ':') {
        throw 'Pass -Command when the scripts directory is not on a redirected local drive.'
    }
    $relativeAgentPath = $agentPath.Substring($driveRoot.Length)
    $redirectedAgentPath =
        "\\tsclient\$($driveRoot[0])\$relativeAgentPath"
    $Command =
        "powershell -NoP -EP Bypass -File `"$redirectedAgentPath`""
}

if ([Threading.Thread]::CurrentThread.ApartmentState -ne 'STA') {
    throw 'Run this bootstrap from an STA Windows PowerShell process.'
}

Add-Type -AssemblyName System.Windows.Forms
Add-Type @'
using System;
using System.Runtime.InteropServices;

public static class SafeRdpBootstrap
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

$readyDirectory = Split-Path -Parent $ReadyPath
New-Item -ItemType Directory -Path $readyDirectory -Force | Out-Null
Remove-Item -LiteralPath $ReadyPath -Force -ErrorAction SilentlyContinue

$idleDeadline = (Get-Date).AddSeconds(30)
while ([SafeRdpBootstrap]::IdleSeconds() -lt 2 -and
    (Get-Date) -lt $idleDeadline) {
    Start-Sleep -Milliseconds 200
}
if ([SafeRdpBootstrap]::IdleSeconds() -lt 2) {
    throw 'The host did not become idle enough for the RDP bootstrap.'
}

$rdpProcess = Get-Process -Id $RdpProcessId -ErrorAction Stop
$rdpWindow = $rdpProcess.MainWindowHandle
if ($rdpWindow -eq [IntPtr]::Zero) {
    throw 'The RDP process does not have an interactive window.'
}

$previousForeground = [SafeRdpBootstrap]::GetForegroundWindow()
$rect = New-Object SafeRdpBootstrap+RECT
if (-not [SafeRdpBootstrap]::GetWindowRect($rdpWindow, [ref] $rect)) {
    throw 'Could not read the RDP window rectangle.'
}
$width = $rect.Right - $rect.Left
$height = $rect.Bottom - $rect.Top
$clipboard = [Windows.Forms.Clipboard]::GetDataObject()
try {
    [Windows.Forms.Clipboard]::SetText($Command)

    # Keep the authenticated RDP client out of sight while it owns focus for
    # the few hundred milliseconds needed to start the guest-side bridge.
    [void] [SafeRdpBootstrap]::SetWindowPos(
        $rdpWindow,
        [IntPtr]::Zero,
        -4000,
        0,
        $width,
        $height,
        0x0014)
    [SafeRdpBootstrap]::Key(0x12, $false)
    [SafeRdpBootstrap]::Key(0x12, $true)
    [void] [SafeRdpBootstrap]::SetForegroundWindow($rdpWindow)
    Start-Sleep -Milliseconds 350

    if ($OpenRunFirst) {
        [SafeRdpBootstrap]::Key(0x5B, $false)
        [SafeRdpBootstrap]::Key(0x52, $false)
        [SafeRdpBootstrap]::Key(0x52, $true)
        [SafeRdpBootstrap]::Key(0x5B, $true)
        Start-Sleep -Milliseconds 900
    }

    [SafeRdpBootstrap]::Key(0x11, $false)
    [SafeRdpBootstrap]::Key(0x56, $false)
    [SafeRdpBootstrap]::Key(0x56, $true)
    [SafeRdpBootstrap]::Key(0x11, $true)
    Start-Sleep -Milliseconds 250
    [SafeRdpBootstrap]::Key(0x0D, $false)
    [SafeRdpBootstrap]::Key(0x0D, $true)
    Start-Sleep -Milliseconds 400
}
finally {
    [void] [SafeRdpBootstrap]::SetWindowPos(
        $rdpWindow,
        [IntPtr]::Zero,
        $rect.Left,
        $rect.Top,
        $width,
        $height,
        0x0014)
    if ($previousForeground -ne [IntPtr]::Zero) {
        [SafeRdpBootstrap]::Key(0x12, $false)
        [SafeRdpBootstrap]::Key(0x12, $true)
        [void] [SafeRdpBootstrap]::SetForegroundWindow($previousForeground)
    }
    if ($null -ne $clipboard) {
        [Windows.Forms.Clipboard]::SetDataObject($clipboard, $true)
    }
}

if ($SkipReadyWait) {
    return
}

$readyDeadline = (Get-Date).AddSeconds(15)
while (-not (Test-Path -LiteralPath $ReadyPath) -and
    (Get-Date) -lt $readyDeadline) {
    Start-Sleep -Milliseconds 250
}
if (-not (Test-Path -LiteralPath $ReadyPath)) {
    throw 'The guest command agent did not report ready.'
}

Get-Content -LiteralPath $ReadyPath -Raw
