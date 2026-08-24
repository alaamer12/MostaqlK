param(
    [string]$DatabasePath = (Join-Path $env:LOCALAPPDATA 'MostaqlK\Data\mostaqlk.db'),
    [switch]$ConfirmReset
)

$ErrorActionPreference = 'Stop'

if (-not $ConfirmReset) {
    throw 'This is destructive. Re-run with -ConfirmReset after closing MostaqlK.'
}

if (-not (Test-Path -LiteralPath $DatabasePath -PathType Leaf)) {
    throw "Database file was not found: $DatabasePath"
}

$databaseFiles = @(
    $DatabasePath,
    "$DatabasePath-wal",
    "$DatabasePath-shm"
)

Write-Host "Resetting database: $DatabasePath"
foreach ($file in $databaseFiles) {
    if (Test-Path -LiteralPath $file -PathType Leaf) {
        Remove-Item -LiteralPath $file -Force
        Write-Host "Removed: $file"
    }
}

Write-Host 'Database reset completed. The next app launch will recreate the V1 schema.'