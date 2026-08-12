#Requires -Assembly UIAutomationClient
#Requires -Assembly UIAutomationTypes

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string] $Executable,
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string] $SeedData,
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string] $OutputDirectory,
    [string] $Workspace = '',
    [switch] $SkipSetup
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Workspace)) {
    $Workspace = Join-Path $env:TEMP 'BuddyWebsiteCapture'
}
$resolvedWorkspace = [System.IO.Path]::GetFullPath($Workspace)
$temporaryRoot = [System.IO.Path]::GetFullPath($env:TEMP).TrimEnd('\') + '\'
if (-not $resolvedWorkspace.StartsWith(
        $temporaryRoot,
        [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to reset an unexpected guest capture directory: $resolvedWorkspace"
}
foreach ($required in @($Executable, $SeedData)) {
    if (-not (Test-Path -LiteralPath $required)) {
        throw "Required redirected host path is unavailable: $required"
    }
}

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @'
using System;
using System.Runtime.InteropServices;

public static class BuddyCaptureNative
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

    [DllImport("user32.dll")]
    public static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    public static extern void mouse_event(
        uint flags,
        uint x,
        uint y,
        uint data,
        UIntPtr extraInfo);

    [DllImport("user32.dll")]
    public static extern void keybd_event(
        byte virtualKey,
        byte scanCode,
        uint flags,
        UIntPtr extraInfo);
}
'@

$automation = [System.Windows.Automation.AutomationElement]
$treeScope = [System.Windows.Automation.TreeScope]
$condition = [System.Windows.Automation.Condition]
$propertyCondition = [System.Windows.Automation.PropertyCondition]
$invokePattern = [System.Windows.Automation.InvokePattern]
$textPattern = [System.Windows.Automation.TextPattern]
$textEndpoint = [System.Windows.Automation.Text.TextPatternRangeEndpoint]
$textUnit = [System.Windows.Automation.Text.TextUnit]

function Stop-BuddyProcesses {
    Get-Process -ErrorAction SilentlyContinue |
        Where-Object {
            $_.ProcessName -in @('Buddy', 'Buddy-capture')
        } |
        Stop-Process -Force
    Start-Sleep -Milliseconds 500
}

function Reset-Directory {
    param([Parameter(Mandatory)] [string] $Path)

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $workspacePrefix = $resolvedWorkspace.TrimEnd('\') + '\'
    if (-not $fullPath.StartsWith(
            $workspacePrefix,
            [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to reset a path outside the guest capture workspace: $fullPath"
    }
    if (Test-Path -LiteralPath $fullPath) {
        Remove-Item -LiteralPath $fullPath -Recurse -Force
    }
    New-Item -ItemType Directory -Path $fullPath -Force | Out-Null
}

function Wait-ForWindow {
    param(
        [Parameter(Mandatory)] [Diagnostics.Process] $Process,
        [Parameter(Mandatory)] [string] $ExecutablePath,
        [int] $TimeoutSeconds = 180
    )

    $resolvedExecutable = [System.IO.Path]::GetFullPath($ExecutablePath)
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        Start-Sleep -Milliseconds 250
        $candidates = @()
        if (-not $Process.HasExited) {
            $candidates += $Process
        }
        $candidates += @(
            Get-Process -Name Buddy -ErrorAction SilentlyContinue
        )
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

    throw 'Buddy did not expose an interactive guest window after its runtime handoff.'
}

function Show-CaptureWindow {
    param([Parameter(Mandatory)] [IntPtr] $Window)

    $topMost = [IntPtr](-1)
    $notTopMost = [IntPtr](-2)
    [void] [BuddyCaptureNative]::SetWindowPos(
        $Window, $topMost, 150, 70, 1300, 850, 0x0040)
    [void] [BuddyCaptureNative]::SetWindowPos(
        $Window, $notTopMost, 150, 70, 1300, 850, 0x0040)
    [BuddyCaptureNative]::keybd_event(0x12, 0, 0, [UIntPtr]::Zero)
    [BuddyCaptureNative]::keybd_event(0x12, 0, 2, [UIntPtr]::Zero)
    [void] [BuddyCaptureNative]::SetForegroundWindow($Window)
    Start-Sleep -Milliseconds 650
}

function Start-CaptureBuddy {
    param([Parameter(Mandatory)] [string] $DataRoot)

    $processInfo = New-Object Diagnostics.ProcessStartInfo
    $processInfo.FileName = Join-Path $resolvedWorkspace 'Buddy.exe'
    $processInfo.UseShellExecute = $false
    $processInfo.EnvironmentVariables['BUDDY_DATA_ROOT'] = $DataRoot
    $processInfo.EnvironmentVariables['BUDDY_AI_ROOT'] =
        (Join-Path $resolvedWorkspace 'language-models')
    $process = [Diagnostics.Process]::Start($processInfo)
    $windowResult = Wait-ForWindow `
        -Process $process `
        -ExecutablePath $processInfo.FileName
    Show-CaptureWindow -Window $windowResult.Window
    return [pscustomobject]@{
        Process = $windowResult.Process
        Window = $windowResult.Window
        AutomationWindow = $automation::FromHandle($windowResult.Window)
    }
}

function Find-ByAutomationId {
    param(
        [Parameter(Mandatory)]
        [System.Windows.Automation.AutomationElement] $Root,
        [Parameter(Mandatory)]
        [string] $AutomationId
    )

    $match = New-Object $propertyCondition(
        $automation::AutomationIdProperty,
        $AutomationId)
    return $Root.FindFirst($treeScope::Subtree, $match)
}

function Wait-ByAutomationId {
    param(
        [Parameter(Mandatory)]
        [System.Windows.Automation.AutomationElement] $Root,
        [Parameter(Mandatory)]
        [string] $AutomationId,
        [int] $TimeoutSeconds = 90,
        [switch] $Visible
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        $element = Find-ByAutomationId -Root $Root -AutomationId $AutomationId
        if ($null -ne $element) {
            try {
                if (-not $Visible -or -not $element.Current.IsOffscreen) {
                    return $element
                }
            }
            catch [System.Windows.Automation.ElementNotAvailableException] {
            }
        }
        Start-Sleep -Milliseconds 200
    } while ((Get-Date) -lt $deadline)
    throw "Timed out waiting for automation element '$AutomationId'."
}

function Invoke-AutomationElement {
    param(
        [Parameter(Mandatory)]
        [System.Windows.Automation.AutomationElement] $Element
    )

    $pattern = $Element.GetCurrentPattern($invokePattern::Pattern)
    $pattern.Invoke()
}

function Click-AutomationElement {
    param(
        [Parameter(Mandatory)]
        [System.Windows.Automation.AutomationElement] $Element
    )

    $rect = $Element.Current.BoundingRectangle
    if ($rect.Width -le 0 -or $rect.Height -le 0) {
        throw 'The automation element does not have a clickable rectangle.'
    }
    Click-Point `
        -X ($rect.Left + ($rect.Width / 2)) `
        -Y ($rect.Top + ($rect.Height / 2))
    Start-Sleep -Milliseconds 450
}

function Wait-ByName {
    param(
        [Parameter(Mandatory)]
        [System.Windows.Automation.AutomationElement] $Root,
        [Parameter(Mandatory)]
        [string] $Name,
        [int] $TimeoutSeconds = 90,
        [switch] $Visible
    )

    $nameMatch = New-Object $propertyCondition(
        $automation::NameProperty,
        $Name)
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        $matches = $Root.FindAll($treeScope::Subtree, $nameMatch)
        foreach ($match in $matches) {
            try {
                if (-not $Visible -or -not $match.Current.IsOffscreen) {
                    return $match
                }
            }
            catch [System.Windows.Automation.ElementNotAvailableException] {
            }
        }
        Start-Sleep -Milliseconds 200
    } while ((Get-Date) -lt $deadline)
    throw "Timed out waiting for automation element named '$Name'."
}

function Capture-BuddyWindow {
    param(
        [Parameter(Mandatory)] [IntPtr] $Window,
        [Parameter(Mandatory)] [string] $FileName
    )

    Show-CaptureWindow -Window $Window
    $rect = New-Object BuddyCaptureNative+RECT
    if (-not [BuddyCaptureNative]::GetWindowRect($Window, [ref] $rect)) {
        throw 'Could not read the Buddy window rectangle.'
    }
    # WinUI's transparent resize gutter otherwise reveals a few pixels of the
    # desktop around an authentic window capture.
    $captureLeft = $rect.Left + 8
    $captureTop = $rect.Top
    $width = ($rect.Right - $rect.Left) - 16
    $height = ($rect.Bottom - $rect.Top) - 8
    $bitmap = New-Object Drawing.Bitmap($width, $height)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.CopyFromScreen(
            $captureLeft,
            $captureTop,
            0,
            0,
            (New-Object Drawing.Size($width, $height)),
            [Drawing.CopyPixelOperation]::SourceCopy)
        $path = Join-Path $OutputDirectory $FileName
        $bitmap.Save($path, [Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

function Click-Point {
    param(
        [Parameter(Mandatory)] [double] $X,
        [Parameter(Mandatory)] [double] $Y,
        [switch] $Right
    )

    [void] [BuddyCaptureNative]::SetCursorPos(
        [Math]::Round($X),
        [Math]::Round($Y))
    if ($Right) {
        [BuddyCaptureNative]::mouse_event(0x0008, 0, 0, 0, [UIntPtr]::Zero)
        [BuddyCaptureNative]::mouse_event(0x0010, 0, 0, 0, [UIntPtr]::Zero)
    }
    else {
        [BuddyCaptureNative]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)
        [BuddyCaptureNative]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)
    }
}

function Invoke-TrayMenuItem {
    param(
        [Parameter(Mandatory)] [string] $Name,
        [Parameter(Mandatory)] [IntPtr] $BuddyWindow
    )

    $desktop = $automation::RootElement
    $all = $desktop.FindAll($treeScope::Descendants, $condition::TrueCondition)
    $trayIcon = $null
    foreach ($element in $all) {
        try {
            if ($element.Current.ClassName -eq 'NotifyItemIcon' -and
                $element.Current.Name.StartsWith(
                    'Chitchat Buddy',
                    [System.StringComparison]::OrdinalIgnoreCase)) {
                $trayIcon = $element
                break
            }
        }
        catch [System.Windows.Automation.ElementNotAvailableException] {
        }
    }
    if ($null -eq $trayIcon) {
        throw 'The Chitchat Buddy notification-area icon was not found.'
    }

    $iconRect = $trayIcon.Current.BoundingRectangle
    Click-Point `
        -X ($iconRect.Left + ($iconRect.Width / 2)) `
        -Y ($iconRect.Top + ($iconRect.Height / 2)) `
        -Right

    $deadline = (Get-Date).AddSeconds(8)
    $menuItem = $null
    do {
        $nameMatch = New-Object $propertyCondition(
            $automation::NameProperty,
            $Name)
        $menuItem = $desktop.FindFirst($treeScope::Descendants, $nameMatch)
        if ($null -eq $menuItem) {
            Start-Sleep -Milliseconds 150
        }
    } while ($null -eq $menuItem -and (Get-Date) -lt $deadline)
    if ($null -eq $menuItem) {
        throw "Tray menu item '$Name' did not appear."
    }

    try {
        $pattern = $menuItem.GetCurrentPattern($invokePattern::Pattern)
        $pattern.Invoke()
    }
    catch {
        $menuRect = $menuItem.Current.BoundingRectangle
        Click-Point `
            -X ($menuRect.Left + ($menuRect.Width / 2)) `
            -Y ($menuRect.Top + ($menuRect.Height / 2))
    }
    Start-Sleep -Milliseconds 900
    Show-CaptureWindow -Window $BuddyWindow
}

function Find-RichTextContaining {
    param(
        [Parameter(Mandatory)]
        [System.Windows.Automation.AutomationElement] $Root,
        [Parameter(Mandatory)]
        [string] $Text
    )

    $all = $Root.FindAll($treeScope::Subtree, $condition::TrueCondition)
    foreach ($element in $all) {
        try {
            if ($element.Current.ClassName -ne 'RichTextBlock') {
                continue
            }
            $pattern = $element.GetCurrentPattern($textPattern::Pattern)
            $content = $pattern.DocumentRange.GetText(-1)
            if ($content.IndexOf(
                    $Text,
                    [System.StringComparison]::OrdinalIgnoreCase) -ge 0) {
                return [pscustomobject]@{
                    Element = $element
                    Pattern = $pattern
                    Content = $content
                }
            }
        }
        catch [System.Windows.Automation.ElementNotAvailableException] {
        }
        catch [System.InvalidOperationException] {
        }
    }
    return $null
}

function Click-Word {
    param(
        [Parameter(Mandatory)]
        [System.Windows.Automation.AutomationElement] $Root,
        [Parameter(Mandatory)]
        [string] $Word
    )

    $deadline = (Get-Date).AddSeconds(20)
    $richText = $null
    do {
        $richText = Find-RichTextContaining -Root $Root -Text $Word
        if ($null -eq $richText) {
            Start-Sleep -Milliseconds 250
        }
    } while ($null -eq $richText -and (Get-Date) -lt $deadline)
    if ($null -eq $richText) {
        throw "No rendered message containing '$Word' was found."
    }

    $index = $richText.Content.IndexOf(
        $Word,
        [System.StringComparison]::OrdinalIgnoreCase)
    $range = $richText.Pattern.DocumentRange.Clone()
    [void] $range.MoveEndpointByUnit(
        $textEndpoint::Start,
        $textUnit::Character,
        $index)
    $range.MoveEndpointByRange(
        $textEndpoint::End,
        $range,
        $textEndpoint::Start)
    [void] $range.MoveEndpointByUnit(
        $textEndpoint::End,
        $textUnit::Character,
        $Word.Length)
    $rectangles = $range.GetBoundingRectangles()
    if ($rectangles.Count -lt 4) {
        throw "The rendered word '$Word' does not have a clickable rectangle."
    }
    Click-Point `
        -X ($rectangles[0] + ($rectangles[2] / 2)) `
        -Y ($rectangles[1] + ($rectangles[3] / 2))
}

function Wait-ForWordLookup {
    param(
        [Parameter(Mandatory)]
        [System.Windows.Automation.AutomationElement] $Root
    )

    [void] (Wait-ByAutomationId `
        -Root $Root `
        -AutomationId 'DismissWordLookupButton' `
        -TimeoutSeconds 12)
    $deadline = (Get-Date).AddSeconds(45)
    do {
        $loading = $false
        $all = $Root.FindAll($treeScope::Subtree, $condition::TrueCondition)
        foreach ($element in $all) {
            try {
                if (-not $element.Current.IsOffscreen -and
                    $element.Current.Name -in @(
                        'Looking up meaning…',
                        'Preparing phonetics…')) {
                    $loading = $true
                    break
                }
            }
            catch [System.Windows.Automation.ElementNotAvailableException] {
            }
        }
        if ($loading) {
            Start-Sleep -Milliseconds 300
        }
    } while ($loading -and (Get-Date) -lt $deadline)
    Start-Sleep -Milliseconds 500
}

$setupData = Join-Path $resolvedWorkspace 'setup-data'
$demoData = Join-Path $resolvedWorkspace 'demo-data'
$guestExecutable = Join-Path $resolvedWorkspace 'Buddy.exe'
$result = [ordered]@{
    Success = $false
    StartedAt = (Get-Date).ToString('o')
    ComputerName = $env:COMPUTERNAME
    SessionId = (Get-Process -Id $PID).SessionId
    Screenshots = @()
}

try {
    Stop-BuddyProcesses
    New-Item -ItemType Directory -Path $resolvedWorkspace -Force | Out-Null
    New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
    Copy-Item -LiteralPath $Executable -Destination $guestExecutable -Force

    if (-not $SkipSetup) {
        Reset-Directory -Path $setupData
        $setup = Start-CaptureBuddy -DataRoot $setupData
        [void] (Wait-ByAutomationId `
            -Root $setup.AutomationWindow `
            -AutomationId 'OnboardingInterfaceLanguagePicker' `
            -Visible)
        Capture-BuddyWindow `
            -Window $setup.Window `
            -FileName '00-welcome-setup.png'
        $result.Screenshots += '00-welcome-setup.png'
        Stop-BuddyProcesses
    }

    Reset-Directory -Path $demoData
    Copy-Item -Path (Join-Path $SeedData '*') `
        -Destination $demoData `
        -Recurse `
        -Force
    $demo = Start-CaptureBuddy -DataRoot $demoData
    [void] (Wait-ByAutomationId `
        -Root $demo.AutomationWindow `
        -AutomationId 'StartDialogChoiceButton' `
        -Visible)
    Capture-BuddyWindow `
        -Window $demo.Window `
        -FileName '01-choose-mode.png'
    $result.Screenshots += '01-choose-mode.png'

    $dialogChoice = Wait-ByAutomationId `
        -Root $demo.AutomationWindow `
        -AutomationId 'StartDialogChoiceButton' `
        -Visible
    Click-AutomationElement -Element $dialogChoice
    [void] (Wait-ByName `
        -Root $demo.AutomationWindow `
        -Name 'Conversation' `
        -Visible)
    [void] (Wait-ByAutomationId `
        -Root $demo.AutomationWindow `
        -AutomationId 'DialogAllowedPausePicker' `
        -Visible)
    try {
        $pauseSetup = Wait-ByName `
            -Root $demo.AutomationWindow `
            -Name 'Pause' `
            -TimeoutSeconds 8 `
            -Visible
        Click-AutomationElement -Element $pauseSetup
    }
    catch {
        # A verified model cache can make the setup notice unnecessary.
    }
    Click-Word -Root $demo.AutomationWindow -Word 'nuance'
    Wait-ForWordLookup -Root $demo.AutomationWindow
    Capture-BuddyWindow `
        -Window $demo.Window `
        -FileName '02-dialog-word-guide.png'
    $result.Screenshots += '02-dialog-word-guide.png'

    $speakTab = Wait-ByAutomationId `
        -Root $demo.AutomationWindow `
        -AutomationId 'SpeakTabButton' `
        -Visible
    Click-AutomationElement -Element $speakTab
    $monologueChoice = Wait-ByAutomationId `
        -Root $demo.AutomationWindow `
        -AutomationId 'StartMonologueChoiceButton' `
        -Visible
    Click-AutomationElement -Element $monologueChoice
    [void] (Wait-ByName `
        -Root $demo.AutomationWindow `
        -Name 'Better version' `
        -Visible)
    Start-Sleep -Milliseconds 1200
    Capture-BuddyWindow `
        -Window $demo.Window `
        -FileName '03-monologue-improvement.png'
    $result.Screenshots += '03-monologue-improvement.png'

    $result.Success = $true
    $result.CompletedAt = (Get-Date).ToString('o')
}
catch {
    $result.Success = $false
    $result.CompletedAt = (Get-Date).ToString('o')
    $result.Error = $_.Exception.Message
    if ($null -ne $demo -and $demo.Window -ne [IntPtr]::Zero) {
        try {
            Capture-BuddyWindow `
                -Window $demo.Window `
                -FileName 'debug-capture-failure.png'
        }
        catch {
        }
    }
}
finally {
    Stop-BuddyProcesses
    $resultPath = Join-Path $OutputDirectory 'capture-result.json'
    $result | ConvertTo-Json -Depth 7 |
        Set-Content -LiteralPath $resultPath -Encoding UTF8
}

$result | ConvertTo-Json -Depth 7
if (-not $result.Success) {
    exit 1
}
