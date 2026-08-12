param(
    [Parameter(Mandatory)]
    [string] $FileName,

    [string] $OutputDirectory = ''
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $PSScriptRoot '..\site\screenshots'
}
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class CurrentBuddyCaptureNative
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
    public static extern bool PrintWindow(
        IntPtr window,
        IntPtr deviceContext,
        uint flags);
}
'@

$process = Get-Process -Name Buddy -ErrorAction Stop |
    Where-Object { $_.MainWindowHandle -ne [IntPtr]::Zero } |
    Select-Object -First 1
if ($null -eq $process) {
    throw 'No interactive Buddy window is running.'
}

$rect = New-Object CurrentBuddyCaptureNative+RECT
[void] [CurrentBuddyCaptureNative]::GetWindowRect(
    $process.MainWindowHandle,
    [ref] $rect)
$fullWidth = $rect.Right - $rect.Left
$fullHeight = $rect.Bottom - $rect.Top
$width = $fullWidth - 16
$height = $fullHeight - 8
$windowBitmap = New-Object Drawing.Bitmap($fullWidth, $fullHeight)
$windowGraphics = [Drawing.Graphics]::FromImage($windowBitmap)
try {
    $deviceContext = $windowGraphics.GetHdc()
    try {
        if (-not [CurrentBuddyCaptureNative]::PrintWindow(
                $process.MainWindowHandle,
                $deviceContext,
                2)) {
            throw 'PrintWindow did not capture Buddy.'
        }
    }
    finally {
        $windowGraphics.ReleaseHdc($deviceContext)
    }

    $crop = New-Object Drawing.Rectangle(8, 0, $width, $height)
    $bitmap = $windowBitmap.Clone($crop, $windowBitmap.PixelFormat)
    $path = Join-Path $outputDirectory $FileName
    try {
        $bitmap.Save($path, [Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $bitmap.Dispose()
    }
    [pscustomobject]@{
        Path = $path
        Width = $width
        Height = $height
    } | ConvertTo-Json
}
finally {
    $windowGraphics.Dispose()
    $windowBitmap.Dispose()
}
