[CmdletBinding()]
param(
    [string] $SshTarget = "rs",
    [string] $RemoteRoot = "/root/buddy-proxy",
    [string] $Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$expectedRoot = "/root/buddy-proxy"
if (-not $RemoteRoot.Equals($expectedRoot, [System.StringComparison]::Ordinal)) {
    throw "RemoteRoot must remain exactly '$expectedRoot'."
}

$repositoryRoot = [System.IO.Path]::GetFullPath(
    (Join-Path $PSScriptRoot ".."))
$projectPath = Join-Path $repositoryRoot "src\Buddy.Proxy\Buddy.Proxy.csproj"
$artifactRoot = Join-Path $repositoryRoot "artifacts\proxy\linux-x64"
$deployFiles = Join-Path $repositoryRoot "deploy\buddy-proxy"
$stagingName = ".deploy-" + [Guid]::NewGuid().ToString("N")
$remoteStaging = "$RemoteRoot/$stagingName"

if (Test-Path -LiteralPath $artifactRoot) {
    $resolvedArtifactRoot = [System.IO.Path]::GetFullPath($artifactRoot)
    $expectedPrefix = [System.IO.Path]::GetFullPath(
        (Join-Path $repositoryRoot "artifacts")) + [System.IO.Path]::DirectorySeparatorChar
    if (-not $resolvedArtifactRoot.StartsWith(
            $expectedPrefix,
            [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Proxy artifact cleanup escaped the repository artifacts directory."
    }
    Remove-Item -LiteralPath $resolvedArtifactRoot -Recurse -Force
}
[System.IO.Directory]::CreateDirectory($artifactRoot) | Out-Null

dotnet publish $projectPath `
    --configuration $Configuration `
    --runtime linux-x64 `
    --self-contained true `
    --output $artifactRoot `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) {
    throw "Buddy proxy publish failed with exit code $LASTEXITCODE."
}

$executable = Join-Path $artifactRoot "buddy-proxy"
$settings = Join-Path $artifactRoot "appsettings.json"
if (-not (Test-Path -LiteralPath $executable -PathType Leaf) `
    -or -not (Test-Path -LiteralPath $settings -PathType Leaf)) {
    throw "The proxy publish did not produce its executable and base settings."
}

$resolvedRemote = (& ssh $SshTarget "readlink -f '$RemoteRoot'").Trim()
if ($LASTEXITCODE -ne 0 -or -not $resolvedRemote.Equals(
        $expectedRoot,
        [System.StringComparison]::Ordinal)) {
    throw "The remote target did not resolve to '$expectedRoot'."
}

& ssh $SshTarget "set -eu; mkdir -p '$remoteStaging'; chmod 700 '$remoteStaging'"
if ($LASTEXITCODE -ne 0) {
    throw "Could not create the target-local deployment staging directory."
}

try {
    & scp $executable $settings `
        (Join-Path $deployFiles "start.sh") `
        (Join-Path $deployFiles "stop.sh") `
        (Join-Path $deployFiles "status.sh") `
        "${SshTarget}:$remoteStaging/"
    if ($LASTEXITCODE -ne 0) {
        throw "Copying Buddy proxy files failed with exit code $LASTEXITCODE."
    }

    $activate = @"
set -eu
ROOT='$RemoteRoot'
STAGE='$remoteStaging'
case "`$(readlink -f "`$STAGE")" in
  "`$ROOT"/.deploy-*) ;;
  *) echo 'Deployment staging escaped the target directory.' >&2; exit 1 ;;
esac
if [ -x "`$ROOT/stop.sh" ]; then "`$ROOT/stop.sh"; fi
mkdir -p "`$ROOT/releases" "`$ROOT/logs" "`$ROOT/run" "`$ROOT/data" "`$ROOT/private"
if [ -f "`$ROOT/buddy-proxy" ]; then
  BACKUP="`$ROOT/releases/`$(date -u +%Y%m%dT%H%M%SZ)"
  mkdir -p "`$BACKUP"
  cp -p "`$ROOT/buddy-proxy" "`$BACKUP/buddy-proxy"
  [ ! -f "`$ROOT/appsettings.json" ] || cp -p "`$ROOT/appsettings.json" "`$BACKUP/appsettings.json"
fi
install -m 700 "`$STAGE/buddy-proxy" "`$ROOT/buddy-proxy"
install -m 600 "`$STAGE/appsettings.json" "`$ROOT/appsettings.json"
install -m 700 "`$STAGE/start.sh" "`$ROOT/start.sh"
install -m 700 "`$STAGE/stop.sh" "`$ROOT/stop.sh"
install -m 700 "`$STAGE/status.sh" "`$ROOT/status.sh"
rm -rf -- "`$STAGE"
"`$ROOT/start.sh"
"`$ROOT/status.sh"
"@
    & ssh $SshTarget $activate
    if ($LASTEXITCODE -ne 0) {
        throw "The target-local proxy activation failed with exit code $LASTEXITCODE."
    }
}
finally {
    & ssh $SshTarget "case '$remoteStaging' in '$RemoteRoot'/.deploy-*) rm -rf -- '$remoteStaging' ;; esac" 2>$null
}
