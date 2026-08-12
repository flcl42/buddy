#requires -RunAsAdministrator
#requires -Modules Hyper-V

[CmdletBinding()]
param(
    [string] $VmName = "Buddy-Test",
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string] $VmRoot,
    [string] $InstallerPath = "",
    [string] $PortablePath = "",
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string] $ModelSource,
    [string] $GuestComputerName = "BUDDY-TEST",
    [string] $GuestUser = "BuddyTest",
    [string] $GuestPassword = ""
)

$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($GuestPassword)) {
    throw "Pass the disposable VM password with -GuestPassword; it is intentionally not stored in source."
}
$repositoryRoot = [System.IO.Path]::GetFullPath(
    (Join-Path $PSScriptRoot ".."))
if ([string]::IsNullOrWhiteSpace($InstallerPath)) {
    $InstallerPath = Join-Path $repositoryRoot "artifacts\release\Buddy-Setup.exe"
}
if ([string]::IsNullOrWhiteSpace($PortablePath)) {
    $PortablePath = Join-Path $repositoryRoot "artifacts\release\Buddy.exe"
}

$resolvedVmRoot = [System.IO.Path]::GetFullPath($VmRoot)
$resolvedInstaller = [System.IO.Path]::GetFullPath($InstallerPath)
$resolvedPortable = [System.IO.Path]::GetFullPath($PortablePath)
$resolvedModels = [System.IO.Path]::GetFullPath($ModelSource)
foreach ($requiredFile in @($resolvedInstaller, $resolvedPortable)) {
    if (-not (Test-Path -LiteralPath $requiredFile -PathType Leaf)) {
        throw "Required release file is missing: $requiredFile"
    }
}
if (-not (Test-Path -LiteralPath $resolvedModels -PathType Container)) {
    throw "Verified speech model directory is missing: $resolvedModels"
}

$installerHash = (Get-FileHash -LiteralPath $resolvedInstaller -Algorithm SHA256).Hash
$buddyHash = (Get-FileHash -LiteralPath $resolvedPortable -Algorithm SHA256).Hash
$modelFiles = @(
    Get-ChildItem -LiteralPath $resolvedModels -File -Force |
        Where-Object Name -Match
            '^(ggml-large-v3-turbo|ggml-silero-v6\.2\.0|kokoro-v1\.0-fp32)\.'
)
if ($modelFiles.Count -ne 6) {
    throw "Expected three model files and three verification stamps; found $($modelFiles.Count)."
}

Import-Module Hyper-V -ErrorAction Stop
$vm = Get-VM -Name $VmName -ErrorAction Stop
$vmConfigurationRoot = [System.IO.Path]::GetFullPath($vm.ConfigurationLocation)
if (-not $vmConfigurationRoot.StartsWith(
        $resolvedVmRoot + [System.IO.Path]::DirectorySeparatorChar,
        [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "The VM configuration is outside the requested VM root: $vmConfigurationRoot"
}
if ($vm.State -ne 'Running') {
    Start-VM -Name $VmName | Out-Null
}

$securePassword = ConvertTo-SecureString $GuestPassword -AsPlainText -Force
$credential = New-Object System.Management.Automation.PSCredential(
    "$GuestComputerName\$GuestUser",
    $securePassword)
$deadline = (Get-Date).AddMinutes(5)
$session = $null
while (-not $session -and (Get-Date) -lt $deadline) {
    try {
        $session = New-PSSession -VMName $VmName -Credential $credential -ErrorAction Stop
    }
    catch {
        Start-Sleep -Seconds 5
    }
}
if (-not $session) {
    throw "PowerShell Direct did not become available for $VmName within five minutes."
}

$guestStaging = "C:\BuddySetup\release-20260811"
try {
    Invoke-Command -Session $session -ScriptBlock {
        param($staging)
        New-Item -ItemType Directory -Path $staging -Force | Out-Null
    } -ArgumentList $guestStaging
    Copy-Item -LiteralPath $resolvedInstaller `
        -Destination "$guestStaging\Buddy-Setup.exe" `
        -ToSession $session `
        -Force
    Copy-Item -LiteralPath $resolvedModels `
        -Destination "$guestStaging\models" `
        -ToSession $session `
        -Recurse `
        -Force

    $guestResult = Invoke-Command -Session $session -ScriptBlock {
        param(
            $staging,
            $expectedInstallerHash,
            $expectedBuddyHash)

        $ErrorActionPreference = 'Stop'
        $installer = Join-Path $staging 'Buddy-Setup.exe'
        $installRoot = Join-Path $env:LOCALAPPDATA 'Programs\Chitchat Buddy'
        $buddyPath = Join-Path $installRoot 'Buddy.exe'
        $actualInstallerHash = (
            Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash
        if ($actualInstallerHash -ne $expectedInstallerHash) {
            throw "Guest installer hash mismatch: $actualInstallerHash"
        }

        Get-Process -Name Buddy -ErrorAction SilentlyContinue |
            Stop-Process -Force
        $processDeadline = (Get-Date).AddSeconds(15)
        while ((Get-Process -Name Buddy -ErrorAction SilentlyContinue) -and
            (Get-Date) -lt $processDeadline) {
            Start-Sleep -Milliseconds 250
        }
        if (Get-Process -Name Buddy -ErrorAction SilentlyContinue) {
            throw 'The previous Buddy process did not exit in the guest.'
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
        $actualBuddyHash = (
            Get-FileHash -LiteralPath $buddyPath -Algorithm SHA256).Hash
        if ($actualBuddyHash -ne $expectedBuddyHash) {
            throw "Installed Buddy hash mismatch: $actualBuddyHash"
        }

        $modelDestination = Join-Path $dataRoot 'models'
        New-Item -ItemType Directory -Path $modelDestination -Force | Out-Null
        Copy-Item -Path (Join-Path $staging 'models\*') `
            -Destination $modelDestination `
            -Force

        foreach ($serviceName in @('AudioEndpointBuilder', 'Audiosrv', 'TermService')) {
            Set-Service -Name $serviceName -StartupType Automatic
            Start-Service -Name $serviceName
        }
        $terminalServerPath =
            'HKLM:\SYSTEM\CurrentControlSet\Control\Terminal Server'
        $terminalServicesPolicy =
            'HKLM:\SOFTWARE\Policies\Microsoft\Windows NT\Terminal Services'
        New-Item -Path $terminalServicesPolicy -Force | Out-Null
        New-ItemProperty -Path $terminalServicesPolicy `
            -Name fDisableAudioCapture -PropertyType DWord -Value 0 -Force |
            Out-Null
        New-ItemProperty -Path $terminalServicesPolicy `
            -Name fDisableAudioRedirection -PropertyType DWord -Value 0 -Force |
            Out-Null
        Set-ItemProperty -Path $terminalServerPath -Name fDenyTSConnections -Value 0
        Enable-NetFirewallRule -DisplayGroup 'Remote Desktop'

        $taskName = 'Chitchat-Buddy-Acceptance-Launch'
        $action = New-ScheduledTaskAction -Execute $buddyPath
        $trigger = New-ScheduledTaskTrigger -AtLogOn -User $env:USERNAME
        $principal = New-ScheduledTaskPrincipal `
            -UserId $env:USERNAME `
            -LogonType Interactive `
            -RunLevel Limited
        Register-ScheduledTask `
            -TaskName $taskName `
            -Action $action `
            -Trigger $trigger `
            -Principal $principal `
            -Force | Out-Null

        [pscustomobject]@{
            ComputerName = $env:COMPUTERNAME
            InstallerExitCode = $install.ExitCode
            InstallerHash = $actualInstallerHash
            BuddyPath = $buddyPath
            BuddyHash = $actualBuddyHash
            StateBackup = $stateBackup
            ModelBytes = (
                Get-ChildItem -LiteralPath $modelDestination -File -Force |
                    Measure-Object Length -Sum).Sum
            AudioEndpointBuilder = (Get-Service AudioEndpointBuilder).Status.ToString()
            WindowsAudio = (Get-Service Audiosrv).Status.ToString()
            RemoteDesktop = (
                (Get-ItemProperty $terminalServerPath).fDenyTSConnections -eq 0)
            AudioCapture = (
                (Get-ItemProperty $terminalServicesPolicy).fDisableAudioCapture -eq 0)
            AudioPlayback = (
                (Get-ItemProperty $terminalServicesPolicy).fDisableAudioRedirection -eq 0)
            LaunchTask = $taskName
        }
    } -ArgumentList $guestStaging, $installerHash, $buddyHash
}
finally {
    if ($session) {
        Remove-PSSession $session
    }
}

$ipDeadline = (Get-Date).AddMinutes(3)
$guestIp = $null
while (-not $guestIp -and (Get-Date) -lt $ipDeadline) {
    $guestIp = Get-VMNetworkAdapter -VMName $VmName |
        ForEach-Object IPAddresses |
        Where-Object {
            $_ -match '^\d{1,3}(\.\d{1,3}){3}$' -and
            $_ -notlike '169.254.*' -and
            $_ -ne '127.0.0.1'
        } |
        Select-Object -First 1
    if (-not $guestIp) {
        Start-Sleep -Seconds 5
    }
}
if (-not $guestIp) {
    throw "No usable IPv4 address was reported for $VmName."
}

$rdpPath = Join-Path $resolvedVmRoot 'Buddy-Test.rdp'
$rdp = Get-Content -LiteralPath $rdpPath -Raw
$rdp = [regex]::Replace(
    $rdp,
    '(?m)^full address:s:.*$',
    "full address:s:$guestIp")
Set-Content -LiteralPath $rdpPath -Value $rdp -Encoding Unicode

$result = [ordered]@{
    Success = $true
    CompletedAt = (Get-Date).ToString('o')
    VmName = $VmName
    VmState = (Get-VM -Name $VmName).State.ToString()
    GuestIp = $guestIp
    RdpPath = $rdpPath
    InstallerHash = $installerHash
    BuddyHash = $buddyHash
    ModelSourceBytes = ($modelFiles | Measure-Object Length -Sum).Sum
    Guest = $guestResult
}
$resultPath = Join-Path $resolvedVmRoot 'update-result.json'
$result | ConvertTo-Json -Depth 6 |
    Set-Content -LiteralPath $resultPath -Encoding UTF8
$result | ConvertTo-Json -Depth 6
