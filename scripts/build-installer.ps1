[CmdletBinding()]
param(
    [string] $Configuration = "Release",
    [string] $Version = "0.4.0",
    [switch] $SkipPublish
)

$ErrorActionPreference = "Stop"
$repositoryRoot = [System.IO.Path]::GetFullPath(
    (Join-Path $PSScriptRoot ".."))
$releaseRoot = Join-Path $repositoryRoot "artifacts\release"
$portablePath = Join-Path $releaseRoot "Buddy.exe"
$installerPath = Join-Path $releaseRoot "Buddy-Setup.exe"
$scriptPath = Join-Path $repositoryRoot "installer\Buddy.iss"
$policyScript = Join-Path $PSScriptRoot "validate-installer-policy.ps1"
$pathPolicyScript = Join-Path $PSScriptRoot "validate-machine-neutral-paths.ps1"

[System.IO.Directory]::CreateDirectory($releaseRoot) | Out-Null

& $pathPolicyScript
& $policyScript

if (-not $SkipPublish) {
    & (Join-Path $PSScriptRoot "publish.ps1") `
        -Configuration $Configuration `
        -OutputPath $portablePath
    if ($LASTEXITCODE -ne 0) {
        throw "Buddy portable publish failed with exit code $LASTEXITCODE."
    }
}

if (-not (Test-Path -LiteralPath $portablePath -PathType Leaf)) {
    throw "Portable release executable is missing: $portablePath"
}

$compilerCandidates = @(
    (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 7\ISCC.exe"),
    (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"),
    (Join-Path ${env:ProgramFiles} "Inno Setup 7\ISCC.exe"),
    (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 7\ISCC.exe"),
    (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"),
    (Join-Path ${env:ProgramFiles} "Inno Setup 6\ISCC.exe")
)
$compiler = $compilerCandidates |
    Where-Object { $_ -and (Test-Path -LiteralPath $_ -PathType Leaf) } |
    Select-Object -First 1
if (-not $compiler) {
    throw "Inno Setup is not installed. Install JRSoftware.InnoSetup with winget, then rerun this script."
}

if (Test-Path -LiteralPath $installerPath) {
    Remove-Item -LiteralPath $installerPath -Force
}

& $compiler "/DMyAppVersion=$Version" $scriptPath
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup compilation failed with exit code $LASTEXITCODE."
}

if (-not (Test-Path -LiteralPath $installerPath -PathType Leaf)) {
    throw "Installer compilation completed without producing $installerPath"
}

& $policyScript -InstallerPath $installerPath

$portableHash = (Get-FileHash -LiteralPath $portablePath -Algorithm SHA256).Hash
$installerHash = (Get-FileHash -LiteralPath $installerPath -Algorithm SHA256).Hash
$portableSize = (Get-Item -LiteralPath $portablePath).Length
$installerSize = (Get-Item -LiteralPath $installerPath).Length

Write-Host ("Portable:  {0} ({1:N1} MiB)" -f $portablePath, ($portableSize / 1MB))
Write-Host "SHA-256:  $portableHash"
Write-Host ("Installer: {0} ({1:N1} MiB)" -f $installerPath, ($installerSize / 1MB))
Write-Host "SHA-256:  $installerHash"
