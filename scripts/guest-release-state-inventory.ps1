[CmdletBinding()]
param(
    [string] $ExpectedInstallerSha256 = '',
    [string[]] $AdditionalPaths = @()
)

$ErrorActionPreference = 'Stop'

$buddyProcesses = @(
    Get-Process -Name Buddy -ErrorAction SilentlyContinue |
        Select-Object Id, Path, MainWindowTitle, Responding)
$candidatePaths = @(
    (Join-Path $env:LOCALAPPDATA 'Programs\Chitchat Buddy\Buddy.exe'),
    (Join-Path $env:LOCALAPPDATA 'Programs\Chitchat Buddy\unins000.exe'),
    (Join-Path $env:LOCALAPPDATA 'Buddy'),
    (Join-Path $env:USERPROFILE 'Desktop\Buddy-Setup.exe'),
    $AdditionalPaths)
$existingPaths = @(
    $candidatePaths |
        Where-Object { Test-Path -LiteralPath $_ } |
        ForEach-Object {
            $item = Get-Item -LiteralPath $_ -Force
            [pscustomobject]@{
                Path = $item.FullName
                Kind = if ($item.PSIsContainer) { 'Directory' } else { 'File' }
                Length = if ($item.PSIsContainer) { $null } else { $item.Length }
            }
        })
$uninstallEntries = @(
    Get-ChildItem @(
        'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall',
        'HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall',
        'HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall'
    ) -ErrorAction SilentlyContinue |
        ForEach-Object { Get-ItemProperty $_.PSPath } |
        Where-Object { $_.DisplayName -like 'Chitchat Buddy*' } |
        Select-Object DisplayName, DisplayVersion, InstallLocation, UninstallString)
$tasks = @(
    Get-ScheduledTask -ErrorAction SilentlyContinue |
        Where-Object {
            $_.TaskName -like '*Buddy*' -or $_.TaskPath -like '*Buddy*'
        } |
        Select-Object TaskPath, TaskName, State)
$desktop = [Environment]::GetFolderPath('Desktop')
$shortcutPath = Join-Path $desktop 'Install Chitchat Buddy.lnk'
$instructionsPath = Join-Path $desktop 'Chitchat Buddy - install me.txt'
$shortcut = $null
if (Test-Path -LiteralPath $shortcutPath) {
    $shell = New-Object -ComObject WScript.Shell
    $link = $shell.CreateShortcut($shortcutPath)
    $targetExists = Test-Path -LiteralPath $link.TargetPath
    $targetHash = if ($targetExists) {
        (Get-FileHash -LiteralPath $link.TargetPath -Algorithm SHA256).Hash
    }
    else {
        $null
    }
    $shortcut = [ordered]@{
        Path = $shortcutPath
        TargetPath = $link.TargetPath
        TargetExists = $targetExists
        TargetSha256 = $targetHash
        HashMatches = [string]::IsNullOrWhiteSpace($ExpectedInstallerSha256) -or
            $targetHash -eq $ExpectedInstallerSha256
    }
}
$audioServices = @(
    Get-Service -Name 'Audiosrv', 'AudioEndpointBuilder' -ErrorAction SilentlyContinue |
        Select-Object Name, Status, StartType)

[ordered]@{
    ComputerName = $env:COMPUTERNAME
    UserName = $env:USERNAME
    SessionId = (Get-Process -Id $PID).SessionId
    Processes = $buddyProcesses
    Paths = $existingPaths
    UninstallEntries = $uninstallEntries
    ScheduledTasks = $tasks
    Desktop = $desktop
    InstallShortcut = $shortcut
    InstructionsPresent = Test-Path -LiteralPath $instructionsPath
    AudioServices = $audioServices
} | ConvertTo-Json -Depth 6
