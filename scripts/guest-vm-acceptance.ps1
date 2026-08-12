[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$installer = '\\tsclient\D\apps\buddy\artifacts\release\Buddy-Setup.exe'
$portable = '\\tsclient\D\apps\buddy\artifacts\release\Buddy.exe'
$modelSource = '\\tsclient\H\Buddy\models'
$hostResult = '\\tsclient\H\Vms\Buddy-Test\rdp-update-result.json'
$installRoot = Join-Path $env:LOCALAPPDATA 'Programs\Chitchat Buddy'
$buddyPath = Join-Path $installRoot 'Buddy.exe'
$result = [ordered]@{
    Success = $false
    StartedAt = (Get-Date).ToString('o')
    ComputerName = $env:COMPUTERNAME
}

try {
    foreach ($required in @($installer, $portable, $modelSource)) {
        if (-not (Test-Path -LiteralPath $required)) {
            throw "Redirected host path is unavailable: $required"
        }
    }
    $installerHash = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash
    $portableHash = (Get-FileHash -LiteralPath $portable -Algorithm SHA256).Hash

    Get-Process -Name Buddy -ErrorAction SilentlyContinue |
        Stop-Process -Force
    $deadline = (Get-Date).AddSeconds(15)
    while ((Get-Process -Name Buddy -ErrorAction SilentlyContinue) -and
        (Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 250
    }
    if (Get-Process -Name Buddy -ErrorAction SilentlyContinue) {
        throw 'The previous Buddy process did not exit.'
    }

    $dataRoot = Join-Path $env:LOCALAPPDATA 'Buddy'
    $stateBackup = $null
    if (Test-Path -LiteralPath $dataRoot) {
        $stateBackup = Join-Path $env:LOCALAPPDATA (
            'Buddy-install-test-backup-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
        Move-Item -LiteralPath $dataRoot -Destination $stateBackup
    }

    $install = Start-Process -FilePath $installer -ArgumentList @(
        '/VERYSILENT',
        '/SUPPRESSMSGBOXES',
        '/NORESTART',
        '/MERGETASKS=!launch'
    ) -Wait -PassThru
    if ($install.ExitCode -notin @(0, 3010)) {
        throw "Buddy installer exited with code $($install.ExitCode)."
    }
    if (-not (Test-Path -LiteralPath $buddyPath -PathType Leaf)) {
        throw "Buddy was not installed at $buddyPath."
    }
    $installedHash = (Get-FileHash -LiteralPath $buddyPath -Algorithm SHA256).Hash
    if ($installedHash -ne $portableHash) {
        throw "Installed Buddy hash mismatch. Expected $portableHash; got $installedHash."
    }

    $modelDestination = Join-Path $dataRoot 'models'
    New-Item -ItemType Directory -Path $modelDestination -Force | Out-Null
    $modelFiles = @(Get-ChildItem -LiteralPath $modelSource -File -Force)
    foreach ($modelFile in $modelFiles) {
        Copy-Item -LiteralPath $modelFile.FullName `
            -Destination (Join-Path $modelDestination $modelFile.Name) `
            -Force
    }

    $buddy = Start-Process -FilePath $buddyPath -PassThru
    $windowDeadline = (Get-Date).AddSeconds(45)
    do {
        Start-Sleep -Milliseconds 300
        $buddy.Refresh()
    } while ($buddy.MainWindowHandle -eq 0 -and
        -not $buddy.HasExited -and
        (Get-Date) -lt $windowDeadline)
    if ($buddy.HasExited -or $buddy.MainWindowHandle -eq 0) {
        throw 'Installed Buddy did not open an interactive window.'
    }

    $audioEndpoints = @(
        Get-PnpDevice -Class AudioEndpoint -PresentOnly -ErrorAction SilentlyContinue |
            Select-Object FriendlyName, Status, InstanceId)
    $soundDevices = @(
        Get-CimInstance Win32_SoundDevice -ErrorAction SilentlyContinue |
            Select-Object Name, Status, DeviceId)
    $result.Success = $true
    $result.CompletedAt = (Get-Date).ToString('o')
    $result.InstallerExitCode = $install.ExitCode
    $result.InstallerHash = $installerHash
    $result.PortableHash = $portableHash
    $result.InstalledHash = $installedHash
    $result.BuddyPath = $buddyPath
    $result.BuddyProcessId = $buddy.Id
    $result.BuddyWindowTitle = $buddy.MainWindowTitle
    $result.BuddyResponding = $buddy.Responding
    $result.StateBackup = $stateBackup
    $result.ModelBytes = (
        Get-ChildItem -LiteralPath $modelDestination -File -Force |
            Measure-Object Length -Sum).Sum
    $result.AudioEndpointBuilder = (Get-Service AudioEndpointBuilder).Status.ToString()
    $result.WindowsAudio = (Get-Service Audiosrv).Status.ToString()
    $result.AudioEndpoints = $audioEndpoints
    $result.SoundDevices = $soundDevices
}
catch {
    $result.Success = $false
    $result.CompletedAt = (Get-Date).ToString('o')
    $result.Error = $_.Exception.Message
}
finally {
    $json = $result | ConvertTo-Json -Depth 8
    $json | Set-Content -LiteralPath $hostResult -Encoding UTF8
    $desktopResult = Join-Path `
        ([Environment]::GetFolderPath('Desktop')) `
        'Chitchat Buddy acceptance result.json'
    $json | Set-Content -LiteralPath $desktopResult -Encoding UTF8
}

if (-not $result.Success) {
    throw $result.Error
}
