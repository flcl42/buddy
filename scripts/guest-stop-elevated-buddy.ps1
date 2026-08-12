$ErrorActionPreference = 'Stop'
$targets = @(
    Get-Process -Name Buddy -ErrorAction SilentlyContinue |
        Select-Object -ExpandProperty Id
)
if ($targets.Count -eq 0) {
    return
}

$idList = $targets -join ','
$command =
    "Get-Process -Id $idList -ErrorAction SilentlyContinue | Stop-Process -Force"
Start-Process `
    -FilePath powershell.exe `
    -Verb RunAs `
    -WindowStyle Hidden `
    -ArgumentList @(
        '-NoLogo',
        '-NoProfile',
        '-ExecutionPolicy',
        'Bypass',
        '-Command',
        ('"{0}"' -f $command)
    )

$deadline = (Get-Date).AddSeconds(15)
while ((Get-Process -Id $targets -ErrorAction SilentlyContinue) -and
    (Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 250
}
if (Get-Process -Id $targets -ErrorAction SilentlyContinue) {
    throw "The elevated stale Buddy process did not exit: $idList"
}
