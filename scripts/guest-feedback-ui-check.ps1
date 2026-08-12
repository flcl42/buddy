[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string] $HostExecutable,
    [string] $Workspace = '',
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string] $HostResultRoot,
    [string] $DataRoot = '',
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string] $SeedData
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Workspace)) {
    $Workspace = Join-Path $env:TEMP 'BuddyFeedbackAcceptance'
}
if ([string]::IsNullOrWhiteSpace($DataRoot)) {
    $DataRoot = Join-Path $Workspace 'data'
}
$resolvedWorkspace = [System.IO.Path]::GetFullPath($Workspace)
$temporaryRoot = [System.IO.Path]::GetFullPath($env:TEMP).TrimEnd('\') + '\'
if (-not $resolvedWorkspace.StartsWith(
        $temporaryRoot,
        [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Unexpected feedback acceptance workspace: $resolvedWorkspace"
}
$Workspace = $resolvedWorkspace
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class BuddyFeedbackWindowNative
{
    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr window);

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
    public static extern bool GetWindowRect(IntPtr window, out Rectangle rectangle);

    [DllImport("user32.dll")]
    public static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    public static extern void mouse_event(
        uint flags,
        uint x,
        uint y,
        uint data,
        UIntPtr extraInfo);

    [StructLayout(LayoutKind.Sequential)]
    public struct Rectangle
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
'@

function Find-ByAutomationId {
    param(
        [Parameter(Mandatory)]
        [System.Windows.Automation.AutomationElement] $Root,

        [Parameter(Mandatory)]
        [string] $AutomationId
    )

    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty,
        $AutomationId)
    return $Root.FindFirst(
        [System.Windows.Automation.TreeScope]::Descendants,
        $condition)
}

function Wait-ByAutomationId {
    param(
        [Parameter(Mandatory)]
        [System.Windows.Automation.AutomationElement] $Root,

        [Parameter(Mandatory)]
        [string] $AutomationId,

        [int] $TimeoutSeconds = 20
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        $element = Find-ByAutomationId -Root $Root -AutomationId $AutomationId
        if ($null -ne $element) {
            return $element
        }

        Start-Sleep -Milliseconds 200
    } while ((Get-Date) -lt $deadline)

    throw "Automation element did not appear: $AutomationId"
}

function Invoke-Element {
    param(
        [Parameter(Mandatory)]
        [System.Windows.Automation.AutomationElement] $Element
    )

    $pattern = $null
    if (-not $Element.TryGetCurrentPattern(
            [System.Windows.Automation.InvokePattern]::Pattern,
            [ref] $pattern)) {
        throw "Element has no invoke pattern: $($Element.Current.AutomationId)"
    }

    ([System.Windows.Automation.InvokePattern] $pattern).Invoke()
}

if (-not (Test-Path -LiteralPath $HostExecutable -PathType Leaf)) {
    throw "Host acceptance executable is unavailable: $HostExecutable"
}
if (-not (Test-Path -LiteralPath (Join-Path $SeedData 'buddy.db') -PathType Leaf)) {
    throw "The host seed data is unavailable: $SeedData"
}

New-Item -ItemType Directory -Path $Workspace -Force | Out-Null
New-Item -ItemType Directory -Path $HostResultRoot -Force | Out-Null
$guestExecutable = Join-Path $Workspace 'Buddy.exe'
Get-Process -Name Buddy -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500
$workspacePrefix = [System.IO.Path]::GetFullPath($Workspace).TrimEnd('\') + '\'
$resolvedDataRoot = [System.IO.Path]::GetFullPath($DataRoot)
if (-not $resolvedDataRoot.StartsWith(
        $workspacePrefix,
        [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "The guest data root escaped the acceptance workspace: $resolvedDataRoot"
}
if (Test-Path -LiteralPath $resolvedDataRoot) {
    Remove-Item -LiteralPath $resolvedDataRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $resolvedDataRoot -Force | Out-Null
Copy-Item -Path (Join-Path $SeedData '*') `
    -Destination $resolvedDataRoot `
    -Recurse `
    -Force
Copy-Item -LiteralPath $HostExecutable -Destination $guestExecutable -Force

$processInfo = New-Object System.Diagnostics.ProcessStartInfo
$processInfo.FileName = $guestExecutable
$processInfo.UseShellExecute = $false
$processInfo.RedirectStandardOutput = $true
$processInfo.RedirectStandardError = $true
$processInfo.EnvironmentVariables['BUDDY_DATA_ROOT'] = $resolvedDataRoot
$processInfo.EnvironmentVariables['BUDDY_AI_ROOT'] =
    (Join-Path $Workspace 'language-models')
$launcher = [System.Diagnostics.Process]::Start($processInfo)

$deadline = (Get-Date).AddSeconds(60)
$process = $null
$window = [IntPtr]::Zero
do {
    Start-Sleep -Milliseconds 250
    foreach ($candidate in @(Get-Process -Name Buddy -ErrorAction SilentlyContinue)) {
        try {
            $candidate.Refresh()
            if ($candidate.Path -eq $guestExecutable -and
                $candidate.MainWindowHandle -ne [IntPtr]::Zero) {
                $process = $candidate
                $window = $candidate.MainWindowHandle
                break
            }
        }
        catch {
        }
    }
} while ($window -eq [IntPtr]::Zero -and (Get-Date) -lt $deadline)
if ($window -eq [IntPtr]::Zero) {
    throw 'Buddy did not open an interactive window in the guest.'
}

[void] [BuddyFeedbackWindowNative]::SetWindowPos(
    $window,
    [IntPtr](-1),
    90,
    50,
    1380,
    900,
    0x0040)
[void] [BuddyFeedbackWindowNative]::SetWindowPos(
    $window,
    [IntPtr](-2),
    90,
    50,
    1380,
    900,
    0x0040)
[void] [BuddyFeedbackWindowNative]::SetForegroundWindow($window)
Start-Sleep -Seconds 2

$root = [System.Windows.Automation.AutomationElement]::FromHandle($window)
$openButton = Wait-ByAutomationId -Root $root -AutomationId 'OpenFeedbackButton'
Invoke-Element -Element $openButton
Start-Sleep -Seconds 2

$rectangle = New-Object BuddyFeedbackWindowNative+Rectangle
if (-not [BuddyFeedbackWindowNative]::GetWindowRect($window, [ref] $rectangle)) {
    throw 'Could not read the Buddy window bounds.'
}

$width = $rectangle.Right - $rectangle.Left
$height = $rectangle.Bottom - $rectangle.Top
$bitmap = New-Object System.Drawing.Bitmap($width, $height)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
try {
    $graphics.CopyFromScreen(
        $rectangle.Left,
        $rectangle.Top,
        0,
        0,
        $bitmap.Size)
    $screenshotPath = Join-Path $HostResultRoot 'feedback-modal.png'
    $bitmap.Save($screenshotPath, [System.Drawing.Imaging.ImageFormat]::Png)
    $attachmentPath = Join-Path $Workspace 'attachment-source.png'
    $bitmap.Save($attachmentPath, [System.Drawing.Imaging.ImageFormat]::Png)
}
finally {
    $graphics.Dispose()
    $bitmap.Dispose()
}

[void] [BuddyFeedbackWindowNative]::SetCursorPos(
    $rectangle.Left + 890,
    $rectangle.Top + 535)
[BuddyFeedbackWindowNative]::mouse_event(
    0x0002,
    0,
    0,
    0,
    [UIntPtr]::Zero)
[BuddyFeedbackWindowNative]::mouse_event(
    0x0004,
    0,
    0,
    0,
    [UIntPtr]::Zero)
Start-Sleep -Seconds 2
[System.Windows.Forms.SendKeys]::SendWait('%n')
Start-Sleep -Milliseconds 300
[System.Windows.Forms.SendKeys]::SendWait($attachmentPath)
[System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
Start-Sleep -Seconds 3

[void] [BuddyFeedbackWindowNative]::SetForegroundWindow($window)
[void] [BuddyFeedbackWindowNative]::SetCursorPos(
    $rectangle.Left + 680,
    $rectangle.Top + 370)
[BuddyFeedbackWindowNative]::mouse_event(
    0x0002,
    0,
    0,
    0,
    [UIntPtr]::Zero)
[BuddyFeedbackWindowNative]::mouse_event(
    0x0004,
    0,
    0,
    0,
    [UIntPtr]::Zero)
Start-Sleep -Milliseconds 300
[System.Windows.Forms.SendKeys]::SendWait(
    'Feedback attachment test from the Hyper V guest.')
Start-Sleep -Seconds 1

$bitmap = New-Object System.Drawing.Bitmap($width, $height)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
try {
    $graphics.CopyFromScreen(
        $rectangle.Left,
        $rectangle.Top,
        0,
        0,
        $bitmap.Size)
    $attachmentScreenshotPath = Join-Path `
        $HostResultRoot `
        'feedback-modal-attached.png'
    $bitmap.Save(
        $attachmentScreenshotPath,
        [System.Drawing.Imaging.ImageFormat]::Png)
}
finally {
    $graphics.Dispose()
    $bitmap.Dispose()
}

$secretDirectory = Join-Path $DataRoot 'secrets'
$processResponding = $process.Responding
$result = [ordered]@{
    Success = $true
    ProcessId = $process.Id
    Executable = $guestExecutable
    WindowTitle = $process.MainWindowTitle
    OpenCommandInvoked = $true
    ProcessResponding = $processResponding
    StoredSecretFiles = if (Test-Path -LiteralPath $secretDirectory) {
        @(Get-ChildItem -LiteralPath $secretDirectory -Filter '*.secret' -File).Count
    }
    else {
        0
    }
    Screenshot = $screenshotPath
    AttachmentScreenshot = $attachmentScreenshotPath
    CheckedAt = (Get-Date).ToString('o')
}
$resultPath = Join-Path $HostResultRoot 'feedback-ui-check.json'
$result | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $resultPath -Encoding UTF8
Get-Process -Name Buddy -ErrorAction SilentlyContinue |
    Where-Object Path -EQ $guestExecutable |
    Stop-Process -Force
$launcher.Dispose()
$result | ConvertTo-Json -Depth 5
