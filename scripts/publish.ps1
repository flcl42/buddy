[CmdletBinding()]
param(
    [string] $Configuration = "Release",
    [string] $RuntimeIdentifier = "win-x64",
    [string] $TargetFramework = "net10.0-windows10.0.19041.0",
    [string] $OutputPath = (Join-Path $env:LOCALAPPDATA "Programs\Chitchat Buddy\Buddy.exe")
)

$ErrorActionPreference = "Stop"
$repositoryRoot = [System.IO.Path]::GetFullPath(
    (Join-Path $PSScriptRoot ".."))
$projectPath = Join-Path $repositoryRoot "src\Buddy.App\Buddy.App.csproj"
$artifactsRoot = [System.IO.Path]::GetFullPath(
    (Join-Path $repositoryRoot "artifacts"))
$resolvedOutput = [System.IO.Path]::GetFullPath($OutputPath)

if (-not $resolvedOutput.EndsWith(
        ".exe",
        [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "OutputPath must name the final Buddy executable."
}

$outputParent = Split-Path -Parent $resolvedOutput
if ([string]::IsNullOrWhiteSpace($outputParent)) {
    throw "OutputPath must have a parent directory."
}

[System.IO.Directory]::CreateDirectory($artifactsRoot) | Out-Null
[System.IO.Directory]::CreateDirectory($outputParent) | Out-Null
$suffix = [Guid]::NewGuid().ToString("N")
$staging = Join-Path $artifactsRoot "publish-staging-$suffix"
$temporaryOutput = Join-Path $outputParent ".Buddy.install-$suffix.exe"
$backupOutput = Join-Path $outputParent ".Buddy.backup-$suffix.exe"
$artifactsPrefix = $artifactsRoot.TrimEnd(
    [System.IO.Path]::DirectorySeparatorChar) `
    + [System.IO.Path]::DirectorySeparatorChar

if (-not $staging.StartsWith(
        $artifactsPrefix,
        [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "The staging directory escaped the repository artifacts folder."
}

$runningInstalled = Get-Process Buddy, Buddy.App -ErrorAction SilentlyContinue |
    Where-Object {
        $_.Path -and
        [System.IO.Path]::GetFullPath($_.Path).Equals(
            $resolvedOutput,
            [System.StringComparison]::OrdinalIgnoreCase)
    }
if ($runningInstalled) {
    throw "Exit the Buddy tray process at '$resolvedOutput' before rebuilding."
}

try {
    # Windows App SDK's generated single-file manifest is incremental but does
    # not include every publish-mode property in its inputs. A clean guarantees
    # that folder publishes can never leave stale activation redirects behind.
    dotnet clean $projectPath `
        --configuration $Configuration `
        --framework $TargetFramework `
        --runtime $RuntimeIdentifier `
        --verbosity quiet `
        --disable-build-servers

    if ($LASTEXITCODE -ne 0) {
        throw "Buddy clean failed with exit code $LASTEXITCODE."
    }

    dotnet publish $projectPath `
        --configuration $Configuration `
        --framework $TargetFramework `
        --runtime $RuntimeIdentifier `
        --output $staging `
        -p:WindowsPackageType=None `
        -p:DebugType=None `
        -p:DebugSymbols=false

    if ($LASTEXITCODE -ne 0) {
        throw "Buddy publish failed with exit code $LASTEXITCODE."
    }

    $stagedFiles = @(Get-ChildItem -LiteralPath $staging -Recurse -File)
    $stagedExecutable = Join-Path $staging "Buddy.exe"
    if ($stagedFiles.Count -ne 1 -or
        -not (Test-Path -LiteralPath $stagedExecutable -PathType Leaf)) {
        $relativeFiles = $stagedFiles |
            ForEach-Object {
                [System.IO.Path]::GetRelativePath($staging, $_.FullName)
            }
        throw "Single-file validation failed. Staged files: $($relativeFiles -join ', ')"
    }

    Copy-Item -LiteralPath $stagedExecutable -Destination $temporaryOutput
    $stagedHash = (Get-FileHash `
            -LiteralPath $stagedExecutable `
            -Algorithm SHA256).Hash
    $temporaryHash = (Get-FileHash `
            -LiteralPath $temporaryOutput `
            -Algorithm SHA256).Hash
    if (-not $stagedHash.Equals(
            $temporaryHash,
            [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "The copied executable failed SHA-256 verification."
    }

    if (Test-Path -LiteralPath $resolvedOutput) {
        Move-Item -LiteralPath $resolvedOutput -Destination $backupOutput
    }

    Move-Item -LiteralPath $temporaryOutput -Destination $resolvedOutput

    if (Test-Path -LiteralPath $backupOutput) {
        Remove-Item -LiteralPath $backupOutput -Force
    }

    $installedHash = (Get-FileHash `
            -LiteralPath $resolvedOutput `
            -Algorithm SHA256).Hash
    if (-not $stagedHash.Equals(
            $installedHash,
            [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "The installed executable failed SHA-256 verification."
    }

    $size = (Get-Item -LiteralPath $resolvedOutput).Length
    Write-Host ("Built one self-contained executable ({0:N1} MiB)." -f
        ($size / 1MB))
    Write-Host "Installed: $resolvedOutput"
    Write-Host "SHA-256:  $installedHash"
}
catch {
    if (Test-Path -LiteralPath $backupOutput) {
        if (Test-Path -LiteralPath $resolvedOutput) {
            Remove-Item -LiteralPath $resolvedOutput -Force
        }

        Move-Item -LiteralPath $backupOutput -Destination $resolvedOutput
    }

    throw
}
finally {
    if (Test-Path -LiteralPath $temporaryOutput) {
        Remove-Item -LiteralPath $temporaryOutput -Force
    }

    if (Test-Path -LiteralPath $staging) {
        Remove-Item -LiteralPath $staging -Recurse -Force
    }
}
