[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [int] $RdpProcessId,

    [Parameter(Mandatory)]
    [AllowEmptyString()]
    [string] $Text,

    [switch] $AltTabFirst,

    [switch] $ControlCFirst,

    [switch] $OpenRunFirst,

    [switch] $PressEnter
)

$ErrorActionPreference = 'Stop'
Add-Type @'
using System;
using System.Runtime.InteropServices;

public static class SafeRdpKeyboard
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

    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT
    {
        public uint type;
        public INPUTUNION data;
    }

    [StructLayout(LayoutKind.Explicit, Size = 32)]
    public struct INPUTUNION
    {
        [FieldOffset(0)]
        public KEYBDINPUT keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT
    {
        public ushort virtualKey;
        public ushort scanCode;
        public uint flags;
        public uint time;
        public UIntPtr extraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint SendInput(
        uint inputCount,
        INPUT[] inputs,
        int inputSize);

    public static double IdleSeconds()
    {
        LASTINPUTINFO input = new LASTINPUTINFO();
        input.cbSize = (uint)Marshal.SizeOf(input);
        GetLastInputInfo(ref input);
        uint elapsed = unchecked((uint)Environment.TickCount - input.dwTime);
        return elapsed / 1000.0;
    }

    public static void Key(byte key)
    {
        keybd_event(key, 0, 0, UIntPtr.Zero);
        keybd_event(key, 0, 2, UIntPtr.Zero);
    }

    public static void AltTab()
    {
        keybd_event(0x12, 0, 0, UIntPtr.Zero);
        Key(0x09);
        keybd_event(0x12, 0, 2, UIntPtr.Zero);
    }

    public static void AltPulse()
    {
        keybd_event(0x12, 0, 0, UIntPtr.Zero);
        keybd_event(0x12, 0, 2, UIntPtr.Zero);
    }

    public static void TypeUnicode(string value)
    {
        foreach (char character in value)
        {
            INPUT[] inputs = new INPUT[2];
            inputs[0].type = 1;
            inputs[0].data.keyboard.scanCode = character;
            inputs[0].data.keyboard.flags = 4;
            inputs[1].type = 1;
            inputs[1].data.keyboard.scanCode = character;
            inputs[1].data.keyboard.flags = 6;
            if (SendInput(2, inputs, Marshal.SizeOf(typeof(INPUT))) != 2)
            {
                throw new InvalidOperationException("Could not send Unicode keyboard input.");
            }
        }
    }
}
'@

$deadline = (Get-Date).AddSeconds(30)
while ([SafeRdpKeyboard]::IdleSeconds() -lt 2 -and (Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 200
}
if ([SafeRdpKeyboard]::IdleSeconds() -lt 2) {
    throw 'The host did not become idle enough for guest keyboard input.'
}

$rdp = (Get-Process -Id $RdpProcessId -ErrorAction Stop).MainWindowHandle
$previous = [SafeRdpKeyboard]::GetForegroundWindow()
$rect = New-Object SafeRdpKeyboard+RECT
[void] [SafeRdpKeyboard]::GetWindowRect($rdp, [ref] $rect)
$width = $rect.Right - $rect.Left
$height = $rect.Bottom - $rect.Top

try {
    [void] [SafeRdpKeyboard]::SetWindowPos(
        $rdp, [IntPtr]::Zero, -4000, 0, $width, $height, 0x0014)
    [SafeRdpKeyboard]::AltPulse()
    [void] [SafeRdpKeyboard]::SetForegroundWindow($rdp)
    Start-Sleep -Milliseconds 600
    if ($AltTabFirst) {
        [SafeRdpKeyboard]::AltTab()
        Start-Sleep -Milliseconds 700
    }
    if ($ControlCFirst) {
        [SafeRdpKeyboard]::keybd_event(0x11, 0, 0, [UIntPtr]::Zero)
        [SafeRdpKeyboard]::Key(0x43)
        [SafeRdpKeyboard]::keybd_event(0x11, 0, 2, [UIntPtr]::Zero)
        Start-Sleep -Seconds 1
    }
    if ($OpenRunFirst) {
        [SafeRdpKeyboard]::keybd_event(0x5B, 0, 0, [UIntPtr]::Zero)
        [SafeRdpKeyboard]::Key(0x52)
        [SafeRdpKeyboard]::keybd_event(0x5B, 0, 2, [UIntPtr]::Zero)
        Start-Sleep -Seconds 1
    }
    [SafeRdpKeyboard]::TypeUnicode($Text)
    if ($PressEnter) {
        [SafeRdpKeyboard]::Key(0x0D)
    }
    Start-Sleep -Seconds 2
}
finally {
    [void] [SafeRdpKeyboard]::SetWindowPos(
        $rdp, [IntPtr]::Zero, $rect.Left, $rect.Top, $width, $height, 0x0014)
    if ($previous -ne [IntPtr]::Zero) {
        [SafeRdpKeyboard]::AltPulse()
        [void] [SafeRdpKeyboard]::SetForegroundWindow($previous)
    }
}
