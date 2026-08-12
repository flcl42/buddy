[CmdletBinding()]
param(
    [string] $InstallerPath = ""
)

$ErrorActionPreference = "Stop"
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$scriptPath = Join-Path $repositoryRoot "installer\Buddy.iss"
$source = Get-Content -LiteralPath $scriptPath -Raw

if ($source -notmatch '(?m)^DefaultDirName=\{localappdata\}\\Programs\\Chitchat Buddy\s*$') {
    throw "The guided installer must default to the current user's local application directory."
}
if ($source -notmatch '(?m)^PrivilegesRequired=lowest\s*$') {
    throw "The guided installer must run without requesting administrator privileges."
}
if ($source -match '(?m)^PrivilegesRequiredOverridesAllowed\s*=') {
    throw "The guided installer must not offer or accept an all-users elevation override."
}
if ($source -match '(?m)^DefaultDirName=\{(?:sd|autopf|commonpf)\}') {
    throw "The guided installer must not default to a machine-wide destination."
}

if (-not [string]::IsNullOrWhiteSpace($InstallerPath)) {
    $resolvedInstaller = [System.IO.Path]::GetFullPath($InstallerPath)
    if (-not (Test-Path -LiteralPath $resolvedInstaller -PathType Leaf)) {
        throw "Installer policy verification could not find: $resolvedInstaller"
    }

    $mt = Get-ChildItem `
            -LiteralPath "${env:ProgramFiles(x86)}\Windows Kits\10\bin" `
            -Filter mt.exe `
            -Recurse `
            -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match '\\x64\\mt\.exe$' } |
        Sort-Object FullName -Descending |
        Select-Object -First 1
    if (-not $mt) {
        throw "Windows SDK mt.exe is required to verify the compiled installer manifest."
    }

    $manifestPath = Join-Path `
        ([System.IO.Path]::GetTempPath()) `
        ("Buddy-Setup-manifest-{0}.xml" -f [Guid]::NewGuid().ToString("N"))
    try {
        & $mt.FullName `
            "-inputresource:$resolvedInstaller;#1" `
            "-out:$manifestPath"
        if ($LASTEXITCODE -ne 0 -or
            -not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
            throw "Could not extract the compiled installer manifest."
        }

        $manifest = Get-Content -LiteralPath $manifestPath -Raw
        if ($manifest -notmatch 'requestedExecutionLevel\s+level="asInvoker"') {
            throw "The compiled installer is not marked to start without elevation."
        }
        if ($manifest -match 'requireAdministrator|highestAvailable') {
            throw "The compiled installer manifest requests elevated privileges."
        }
    }
    finally {
        if (Test-Path -LiteralPath $manifestPath) {
            Remove-Item -LiteralPath $manifestPath -Force
        }
    }
}

Write-Host "Installer policy verified: per-user destination and no elevation request."
