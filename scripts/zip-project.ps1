# Zip the project respecting .gitignore (the .git folder is always included)
param(
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot

if (-not (Get-Command git -ErrorAction SilentlyContinue)) { throw "git is required but was not found in PATH" }
if (-not (Test-Path -LiteralPath (Join-Path $root '.git'))) { throw "No .git found in $root" }

if (-not $OutputPath) {
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $name = Split-Path -Leaf $root
    $OutputPath = Join-Path $root "release_artifacts\$name-$stamp.zip"
}
$OutputPath = [System.IO.Path]::GetFullPath($OutputPath)
$outDir = Split-Path -Parent $OutputPath
if (-not (Test-Path -LiteralPath $outDir)) { New-Item -ItemType Directory -Path $outDir -Force | Out-Null }
if (Test-Path -LiteralPath $OutputPath) { Remove-Item -LiteralPath $OutputPath -Force }

Write-Host "Collecting files (respecting .gitignore)..." -ForegroundColor Cyan
$trackedOrUntracked = & git -C $root -c core.quotePath=false ls-files --cached --others --exclude-standard
if ($LASTEXITCODE -ne 0) { throw "git ls-files failed with exit code $LASTEXITCODE" }

$gitPath = Join-Path $root '.git'
if ((Get-Item -LiteralPath $gitPath -Force).PSIsContainer) {
    $gitEntries = Get-ChildItem -LiteralPath $gitPath -Recurse -Force -File | ForEach-Object {
        $_.FullName.Substring($root.Length).TrimStart('\', '/') -replace '\\', '/'
    }
} else {
    $gitEntries = @('.git')
}

$entries = @($trackedOrUntracked) + @($gitEntries) |
    Where-Object { $_ } |
    Sort-Object -Unique

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

Write-Host "Writing $OutputPath" -ForegroundColor Cyan
$added = 0
$zip = [System.IO.Compression.ZipFile]::Open($OutputPath, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($rel in $entries) {
        $full = Join-Path $root ($rel -replace '/', '\')
        if (-not (Test-Path -LiteralPath $full -PathType Leaf)) { continue }
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $full, $rel, [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
        $added++
    }
} finally {
    $zip.Dispose()
}

$sizeMb = [math]::Round((Get-Item -LiteralPath $OutputPath).Length / 1MB, 2)
Write-Host "Zipped $added files ($sizeMb MB) -> $OutputPath" -ForegroundColor Green
