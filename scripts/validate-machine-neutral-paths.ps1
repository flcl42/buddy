[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$repositoryRoot = [System.IO.Path]::GetFullPath(
    (Join-Path $PSScriptRoot ".."))

$forbiddenPatterns = [ordered]@{
    "machine-specific data drive" = -join @(
        [char]0x48,
        [char]0x3A,
        [char]0x5C)
    "legacy machine-specific install location" = -join @(
        [char]0x43,
        [char]0x3A,
        [char]0x5C,
        "Programs")
}

foreach ($entry in $forbiddenPatterns.GetEnumerator()) {
    $matches = @(
        & git -C $repositoryRoot grep -l -I -F -- $entry.Value
    )
    $grepExitCode = $LASTEXITCODE
    if ($grepExitCode -gt 1) {
        throw "Repository path validation failed while searching tracked files."
    }

    if ($grepExitCode -eq 0) {
        $files = $matches -join ", "
        throw "Tracked files contain a $($entry.Key): $files"
    }
}

$global:LASTEXITCODE = 0
Write-Host "Machine-neutral path policy passed."
