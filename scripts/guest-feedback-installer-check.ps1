[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string] $HostReleaseRoot,
    [string] $Workspace = '',
    [string] $HostResultPath = ''
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Workspace)) {
    $Workspace = Join-Path $env:TEMP 'BuddyInstallerAcceptance'
}
if ([string]::IsNullOrWhiteSpace($HostResultPath)) {
    $HostResultPath = Join-Path $HostReleaseRoot 'installer-acceptance.json'
}
$hostInstaller = Join-Path $HostReleaseRoot 'Buddy-Setup.exe'
$hostPortable = Join-Path $HostReleaseRoot 'Buddy.exe'
foreach ($required in @($hostInstaller, $hostPortable)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
        throw "Release file is unavailable in the guest: $required"
    }
}

$resolvedWorkspace = [System.IO.Path]::GetFullPath($Workspace)
$temporaryRoot = [System.IO.Path]::GetFullPath($env:TEMP).TrimEnd('\') + '\'
if (-not $resolvedWorkspace.StartsWith(
        $temporaryRoot,
        [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to reset an unexpected installer workspace: $resolvedWorkspace"
}

Get-Process -Name Buddy -ErrorAction SilentlyContinue | Stop-Process -Force
$processDeadline = (Get-Date).AddSeconds(15)
while ((Get-Process -Name Buddy -ErrorAction SilentlyContinue) -and
    (Get-Date) -lt $processDeadline) {
    Start-Sleep -Milliseconds 250
}
if (Get-Process -Name Buddy -ErrorAction SilentlyContinue) {
    throw 'The previous Buddy process did not exit in the guest.'
}
Start-Sleep -Milliseconds 750
if (Test-Path -LiteralPath $resolvedWorkspace) {
    Remove-Item -LiteralPath $resolvedWorkspace -Recurse -Force
}
New-Item -ItemType Directory -Path $resolvedWorkspace -Force | Out-Null

$guestInstaller = Join-Path $resolvedWorkspace 'Buddy-Setup.exe'
$installDirectory = Join-Path $resolvedWorkspace 'Installed'
Copy-Item -LiteralPath $hostInstaller -Destination $guestInstaller -Force
$expectedInstallerHash = (
    Get-FileHash -LiteralPath $hostInstaller -Algorithm SHA256).Hash
$guestInstallerHash = (
    Get-FileHash -LiteralPath $guestInstaller -Algorithm SHA256).Hash
if ($guestInstallerHash -ne $expectedInstallerHash) {
    throw 'The installer copy failed SHA-256 verification.'
}

$install = Start-Process -FilePath $guestInstaller -ArgumentList @(
    '/VERYSILENT',
    '/SUPPRESSMSGBOXES',
    '/NORESTART',
    "/DIR=$installDirectory",
    '/MERGETASKS=!launch'
) -Wait -PassThru
if ($install.ExitCode -notin @(0, 3010)) {
    throw "The installer exited with code $($install.ExitCode)."
}

$installedExecutable = Join-Path $installDirectory 'Buddy.exe'
if (-not (Test-Path -LiteralPath $installedExecutable -PathType Leaf)) {
    throw "The installer did not create $installedExecutable."
}
$expectedPortableHash = (
    Get-FileHash -LiteralPath $hostPortable -Algorithm SHA256).Hash
$installedHash = (
    Get-FileHash -LiteralPath $installedExecutable -Algorithm SHA256).Hash
if ($installedHash -ne $expectedPortableHash) {
    throw 'The installed executable differs from the portable release.'
}

$process = Start-Process -FilePath $installedExecutable -PassThru
$deadline = (Get-Date).AddSeconds(60)
$interactive = $null
do {
    Start-Sleep -Milliseconds 250
    foreach ($candidate in @(Get-Process -Name Buddy -ErrorAction SilentlyContinue)) {
        try {
            $candidate.Refresh()
            if ($candidate.Path -eq $installedExecutable -and
                $candidate.MainWindowHandle -ne [IntPtr]::Zero) {
                $interactive = $candidate
                break
            }
        }
        catch {
        }
    }
} while ($null -eq $interactive -and (Get-Date) -lt $deadline)
if ($null -eq $interactive) {
    throw 'The installed release did not open an interactive window.'
}

$result = [ordered]@{
    Success = $true
    InstallerExitCode = $install.ExitCode
    InstallerHash = $guestInstallerHash
    PortableHash = $expectedPortableHash
    InstalledHash = $installedHash
    InstalledPath = $installedExecutable
    ProcessId = $interactive.Id
    WindowTitle = $interactive.MainWindowTitle
    Responding = $interactive.Responding
    CheckedAt = (Get-Date).ToString('o')
}
$result | ConvertTo-Json -Depth 5 |
    Set-Content -LiteralPath $HostResultPath -Encoding UTF8
$result | ConvertTo-Json -Depth 5
