[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string] $HostInstaller,

    [Parameter(Mandatory)]
    [ValidatePattern('^[A-Fa-f0-9]{64}$')]
    [string] $ExpectedSha256,

    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string] $ReleaseVersion,

    [Parameter(Mandatory)]
    [switch] $ConfirmDisposableGuest,

    [string[]] $AcceptanceDirectories = @())

$ErrorActionPreference = 'Stop'
if (-not $ConfirmDisposableGuest) {
    throw 'Explicit disposable-guest confirmation is required before state cleanup.'
}
if ($AcceptanceDirectories.Count -eq 0) {
    $acceptanceRoot = Join-Path $env:TEMP 'BuddyAcceptance'
    $AcceptanceDirectories = @(
        'Installer',
        'Feedback',
        'Input',
        'Window') |
        ForEach-Object { Join-Path $acceptanceRoot $_ }
}
$temporaryRoot = [System.IO.Path]::GetFullPath($env:TEMP).TrimEnd('\') + '\'
foreach ($directory in $AcceptanceDirectories) {
    $resolvedDirectory = [System.IO.Path]::GetFullPath($directory)
    if (-not $resolvedDirectory.StartsWith(
            $temporaryRoot,
            [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Acceptance directory must remain under the guest temporary directory: $resolvedDirectory"
    }
}
$allowedRemovalPaths = @(
    $AcceptanceDirectories
    (Join-Path $env:LOCALAPPDATA 'Buddy')
    (Join-Path $env:TEMP '.net\Buddy')) |
    ForEach-Object { [System.IO.Path]::GetFullPath($_) }

function Remove-ExactDirectory {
    param([Parameter(Mandatory)] [string] $Path)

    $resolved = [System.IO.Path]::GetFullPath($Path)
    if ($resolved -notin $allowedRemovalPaths) {
        throw "Refusing to remove an unexpected directory: $resolved"
    }

    if (Test-Path -LiteralPath $resolved) {
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}

$HostInstaller = [System.IO.Path]::GetFullPath($HostInstaller)
if (-not $HostInstaller.StartsWith(
        '\\tsclient\',
        [System.StringComparison]::OrdinalIgnoreCase) -or
    [System.IO.Path]::GetFileName($HostInstaller) -ne 'Buddy-Setup.exe') {
    throw 'HostInstaller must be a redirected host file named Buddy-Setup.exe.'
}
if (-not (Test-Path -LiteralPath $HostInstaller -PathType Leaf)) {
    throw "The release installer is unavailable through RDP drive redirection: $HostInstaller"
}

$actualHash = (Get-FileHash -LiteralPath $HostInstaller -Algorithm SHA256).Hash
if (-not $actualHash.Equals(
        $ExpectedSha256,
        [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "The release installer hash does not match the published value."
}

Get-Process -Name Buddy -ErrorAction SilentlyContinue | Stop-Process -Force
$processDeadline = (Get-Date).AddSeconds(20)
while ((Get-Process -Name Buddy -ErrorAction SilentlyContinue) -and
    (Get-Date) -lt $processDeadline) {
    Start-Sleep -Milliseconds 250
}
if (Get-Process -Name Buddy -ErrorAction SilentlyContinue) {
    throw 'A Buddy process did not stop before the clean-install reset.'
}

$installRoot = Join-Path $env:LOCALAPPDATA 'Programs\Chitchat Buddy'
$installedBuddy = Join-Path $installRoot 'Buddy.exe'
$uninstaller = Join-Path $installRoot 'unins000.exe'
if ((Test-Path -LiteralPath $uninstaller -PathType Leaf) -and
    (Test-Path -LiteralPath $installedBuddy -PathType Leaf)) {
    $productName = (Get-Item -LiteralPath $installedBuddy).VersionInfo.ProductName
    if ($productName -eq 'Chitchat Buddy') {
        $uninstall = Start-Process -FilePath $uninstaller -ArgumentList @(
            '/VERYSILENT',
            '/SUPPRESSMSGBOXES',
            '/NORESTART'
        ) -Wait -PassThru
        if ($uninstall.ExitCode -notin @(0, 3010)) {
            throw "The previous Buddy uninstaller exited with code $($uninstall.ExitCode)."
        }
    }
}

foreach ($path in $allowedRemovalPaths) {
    Remove-ExactDirectory -Path $path
}

foreach ($backup in @(
    Get-ChildItem -LiteralPath $env:LOCALAPPDATA `
        -Directory `
        -Filter 'Buddy-install-test-backup-*' `
        -ErrorAction SilentlyContinue)) {
    $resolvedBackup = [System.IO.Path]::GetFullPath($backup.FullName)
    $allowedBackupPrefix = [System.IO.Path]::GetFullPath(
        (Join-Path $env:LOCALAPPDATA 'Buddy-install-test-backup-'))
    if (-not $resolvedBackup.StartsWith(
            $allowedBackupPrefix,
            [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "A Buddy backup escaped the expected profile directory: $resolvedBackup"
    }
    Remove-Item -LiteralPath $resolvedBackup -Recurse -Force
}

foreach ($file in @(
    (Join-Path $installRoot 'Buddy.exe'),
    (Join-Path $installRoot 'unins000.exe'),
    (Join-Path $installRoot 'unins000.dat'),
    (Join-Path $installRoot 'unins000.msg'),
    (Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Chitchat Buddy.lnk'),
    (Join-Path ([Environment]::GetFolderPath('Desktop')) 'Chitchat Buddy.lnk'))) {
    if (Test-Path -LiteralPath $file -PathType Leaf) {
        Remove-Item -LiteralPath $file -Force
    }
}

Get-ScheduledTask -TaskName 'Chitchat-Buddy-Acceptance-Launch' `
    -ErrorAction SilentlyContinue |
    Unregister-ScheduledTask -Confirm:$false

$desktop = [Environment]::GetFolderPath('Desktop')
$shortcutPath = Join-Path $desktop 'Install Chitchat Buddy.lnk'
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $HostInstaller
$shortcut.WorkingDirectory = Split-Path -Parent $HostInstaller
$shortcut.Description = "Install Chitchat Buddy $ReleaseVersion"
$shortcut.Save()
[Runtime.InteropServices.Marshal]::FinalReleaseComObject($shortcut) | Out-Null
[Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell) | Out-Null

$instructions = Join-Path $desktop 'Chitchat Buddy - install me.txt'
@(
    "Chitchat Buddy $ReleaseVersion is ready for your manual installation test.",
    '',
    '1. Double-click Install Chitchat Buddy.',
    '2. If SmartScreen appears, confirm that this is the expected GitHub release.',
    '3. Keep the default per-user destination and finish setup.',
    '4. Leave Launch Chitchat Buddy selected to test the welcome screen.',
    '',
    "Installer SHA-256: $actualHash",
    'The installer is unsigned, so Windows may show an unknown-publisher warning.'
) | Set-Content -LiteralPath $instructions -Encoding UTF8

$unexpectedBuddy = @(
    Get-Process -Name Buddy -ErrorAction SilentlyContinue)
$leftoverInstall = Test-Path -LiteralPath $installedBuddy
if ($unexpectedBuddy.Count -ne 0 -or $leftoverInstall) {
    throw 'The guest was not returned to a clean pre-install Buddy state.'
}

[ordered]@{
    Success = $true
    ComputerName = $env:COMPUTERNAME
    UserName = $env:USERNAME
    ReleaseVersion = $ReleaseVersion
    Installer = $HostInstaller
    InstallerSha256 = $actualHash
    BuddyInstalled = $false
    BuddyProcesses = 0
    LocalStatePresent = Test-Path -LiteralPath (Join-Path $env:LOCALAPPDATA 'Buddy')
    Shortcut = $shortcutPath
    Instructions = $instructions
    AudioPlaybackRedirection = $true
    AudioCaptureRedirection = $true
} | ConvertTo-Json -Depth 4
