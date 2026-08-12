#Requires -Assembly UIAutomationClient
#Requires -Assembly UIAutomationTypes

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string] $Executable,
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string] $OutputDirectory,
    [string] $Workspace = ''
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Workspace)) {
    $Workspace = Join-Path $env:TEMP 'BuddyInputStyleAcceptance'
}
$resolvedWorkspace = [System.IO.Path]::GetFullPath($Workspace)
$temporaryRoot = [System.IO.Path]::GetFullPath($env:TEMP).TrimEnd('\') + '\'
if (-not $resolvedWorkspace.StartsWith(
        $temporaryRoot,
        [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to reset an unexpected guest directory: $resolvedWorkspace"
}
if (-not (Test-Path -LiteralPath $Executable -PathType Leaf)) {
    throw "Buddy executable is unavailable: $Executable"
}

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @'
using System;
using System.Runtime.InteropServices;

public static class BuddyInputStyleNative
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
    public static extern bool SetWindowPos(
        IntPtr window,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr window);
}
'@

function Wait-ForWindow {
    param(
        [Parameter(Mandatory)] [Diagnostics.Process] $Process,
        [Parameter(Mandatory)] [string] $ExecutablePath
    )

    $resolvedExecutable = [System.IO.Path]::GetFullPath($ExecutablePath)
    $deadline = (Get-Date).AddMinutes(2)
    do {
        Start-Sleep -Milliseconds 250
        $candidates = @()
        if (-not $Process.HasExited) {
            $candidates += $Process
        }
        $candidates += @(Get-Process -Name Buddy -ErrorAction SilentlyContinue)
        foreach ($candidate in $candidates | Sort-Object Id -Unique) {
            try {
                $candidate.Refresh()
                $candidatePath = [System.IO.Path]::GetFullPath($candidate.Path)
                if ($candidatePath.Equals(
                        $resolvedExecutable,
                        [System.StringComparison]::OrdinalIgnoreCase) -and
                    $candidate.MainWindowHandle -ne [IntPtr]::Zero) {
                    return [pscustomobject]@{
                        Process = $candidate
                        Window = $candidate.MainWindowHandle
                    }
                }
            }
            catch [System.ComponentModel.Win32Exception] {
            }
            catch [System.InvalidOperationException] {
            }
        }
    } while ((Get-Date) -lt $deadline)
    throw 'Buddy did not open its setup window.'
}

function Wait-ForAutomationId {
    param(
        [Parameter(Mandatory)]
        [System.Windows.Automation.AutomationElement] $Root,
        [Parameter(Mandatory)]
        [string] $AutomationId
    )

    $condition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty,
        $AutomationId)
    $deadline = (Get-Date).AddSeconds(30)
    do {
        $element = $Root.FindFirst(
            [System.Windows.Automation.TreeScope]::Subtree,
            $condition)
        if ($null -eq $element) {
            Start-Sleep -Milliseconds 200
        }
    } while ($null -eq $element -and (Get-Date) -lt $deadline)
    if ($null -eq $element) {
        throw "Could not find setup control '$AutomationId'."
    }
    $element
}

function Capture-Window {
    param(
        [Parameter(Mandatory)] [IntPtr] $Window,
        [Parameter(Mandatory)] [string] $OutputPath
    )

    $rect = New-Object BuddyInputStyleNative+RECT
    if (-not [BuddyInputStyleNative]::GetWindowRect($Window, [ref] $rect)) {
        throw 'Could not measure the Buddy window.'
    }
    $width = $rect.Right - $rect.Left
    $height = $rect.Bottom - $rect.Top
    $bitmap = [System.Drawing.Bitmap]::new($width, $height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.CopyFromScreen(
            $rect.Left,
            $rect.Top,
            0,
            0,
            [System.Drawing.Size]::new($width, $height))
        $bitmap.Save($OutputPath, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

$guestExecutable = Join-Path $resolvedWorkspace 'Buddy.exe'
$dataRoot = Join-Path $resolvedWorkspace 'data'
$result = [ordered]@{ Success = $false }
$process = $null
try {
    Get-Process -Name Buddy -ErrorAction SilentlyContinue | Stop-Process -Force
    if (Test-Path -LiteralPath $resolvedWorkspace) {
        Remove-Item -LiteralPath $resolvedWorkspace -Recurse -Force
    }
    New-Item -ItemType Directory -Path $resolvedWorkspace -Force | Out-Null
    New-Item -ItemType Directory -Path $dataRoot -Force | Out-Null
    New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
    Copy-Item -LiteralPath $Executable -Destination $guestExecutable -Force

    $env:BUDDY_DATA_ROOT = $dataRoot
    $env:BUDDY_AI_ROOT = Join-Path $resolvedWorkspace 'language-models'
    $process = Start-Process -FilePath $guestExecutable -PassThru
    $windowResult = Wait-ForWindow `
        -Process $process `
        -ExecutablePath $guestExecutable
    $process = $windowResult.Process
    $window = $windowResult.Window
    [void] [BuddyInputStyleNative]::SetWindowPos(
        $window, [IntPtr](-1), 150, 70, 1300, 850, 0x0040)
    [void] [BuddyInputStyleNative]::SetWindowPos(
        $window, [IntPtr](-2), 150, 70, 1300, 850, 0x0040)
    [void] [BuddyInputStyleNative]::SetForegroundWindow($window)

    $root = [System.Windows.Automation.AutomationElement]::FromHandle($window)
    $picker = Wait-ForAutomationId `
        -Root $root `
        -AutomationId 'OnboardingProviderPicker'
    $pattern = $picker.GetCurrentPattern(
        [System.Windows.Automation.ExpandCollapsePattern]::Pattern)
    $pattern.Expand()
    Start-Sleep -Milliseconds 800

    $outputPath = Join-Path $OutputDirectory 'input-dropdown-open.png'
    Capture-Window -Window $window -OutputPath $outputPath

    $pattern.Collapse()
    $entry = Wait-ForAutomationId `
        -Root $root `
        -AutomationId 'OnboardingTrialCodeEntry'
    $entry.SetFocus()
    Start-Sleep -Milliseconds 500
    $focusedOutputPath = Join-Path $OutputDirectory 'input-entry-focused.png'
    Capture-Window -Window $window -OutputPath $focusedOutputPath

    $result.Success = $true
    $result.Screenshot = $outputPath
    $result.FocusedScreenshot = $focusedOutputPath
    $result.EntryHasKeyboardFocus = $entry.Current.HasKeyboardFocus
}
catch {
    $result.Error = $_.Exception.Message
}
finally {
    Remove-Item Env:BUDDY_DATA_ROOT -ErrorAction SilentlyContinue
    Remove-Item Env:BUDDY_AI_ROOT -ErrorAction SilentlyContinue
    if ($null -ne $process) {
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    }
}

$result | ConvertTo-Json -Depth 4
if (-not $result.Success) {
    exit 1
}
