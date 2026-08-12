[CmdletBinding()]
param(
    [string] $CaptureScript = '',
    [string] $LogPath = '',
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

$ErrorActionPreference = 'Continue'
if ([string]::IsNullOrWhiteSpace($CaptureScript)) {
    $CaptureScript = Join-Path $PSScriptRoot 'guest-capture-website.ps1'
}
if ([string]::IsNullOrWhiteSpace($LogPath)) {
    $LogPath = Join-Path $PSScriptRoot '..\artifacts\vm\capture-diagnostic.log'
}
New-Item -ItemType Directory -Path (Split-Path -Parent $LogPath) -Force |
    Out-Null
try {
    $captureArguments = @{
        Executable = $Executable
        SeedData = $SeedData
        OutputDirectory = $OutputDirectory
        SkipSetup = $SkipSetup
    }
    if (-not [string]::IsNullOrWhiteSpace($Workspace)) {
        $captureArguments.Workspace = $Workspace
    }
    & $CaptureScript @captureArguments *>&1 |
        ForEach-Object { $_ | Out-String } |
        Set-Content -LiteralPath $LogPath -Encoding UTF8
    "ExitCode=$LASTEXITCODE" |
        Add-Content -LiteralPath $LogPath -Encoding UTF8
}
catch {
    $_ | Format-List * -Force | Out-String |
        Set-Content -LiteralPath $LogPath -Encoding UTF8
}

exit 0
