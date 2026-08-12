[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [int] $RdpProcessId,

    [Parameter(Mandatory)]
    [string] $OutputPath
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;

public static class RdpWindowCapture
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr window, out RECT rect);

    [DllImport("user32.dll")]
    public static extern bool PrintWindow(IntPtr window, IntPtr deviceContext, uint flags);
}
'@

$process = Get-Process -Id $RdpProcessId -ErrorAction Stop
$window = $process.MainWindowHandle
if ($window -eq [IntPtr]::Zero) {
    throw "RDP process $RdpProcessId has no main window."
}

$rect = New-Object RdpWindowCapture+RECT
if (-not [RdpWindowCapture]::GetWindowRect($window, [ref] $rect)) {
    throw 'Could not measure the RDP window.'
}

$width = $rect.Right - $rect.Left
$height = $rect.Bottom - $rect.Top
$bitmap = [System.Drawing.Bitmap]::new($width, $height)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
try {
    $deviceContext = $graphics.GetHdc()
    try {
        if (-not [RdpWindowCapture]::PrintWindow($window, $deviceContext, 0)) {
            throw 'PrintWindow did not capture the RDP window.'
        }
    }
    finally {
        $graphics.ReleaseHdc($deviceContext)
    }

    $absoluteOutput = [System.IO.Path]::GetFullPath($OutputPath)
    $directory = [System.IO.Path]::GetDirectoryName($absoluteOutput)
    [System.IO.Directory]::CreateDirectory($directory) | Out-Null
    $bitmap.Save($absoluteOutput, [System.Drawing.Imaging.ImageFormat]::Png)
    $absoluteOutput
}
finally {
    $graphics.Dispose()
    $bitmap.Dispose()
}
