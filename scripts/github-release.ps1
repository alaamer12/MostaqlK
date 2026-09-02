param(
    [string]$Arch = "x64",
    [switch]$Force,
    [switch]$CheckOnly,
    [switch]$ResetDatabase
)

$ErrorActionPreference = "Stop"

$scriptDir = $PSScriptRoot
$repoRoot = Split-Path $scriptDir -Parent
$csprojPath = Join-Path $repoRoot "MostaqlK.csproj"

if (-not (Test-Path $csprojPath)) {
    Write-Error "Could not find MostaqlK.csproj at $csprojPath"
    exit 1
}

# 1. Extract Version from MostaqlK.csproj
[xml]$csproj = Get-Content $csprojPath
$displayVersion = $csproj.Project.PropertyGroup.ApplicationDisplayVersion | Where-Object { $_ } | Select-Object -First 1
$appVersion = $csproj.Project.PropertyGroup.ApplicationVersion | Where-Object { $_ } | Select-Object -First 1

if (-not $displayVersion) {
    $displayVersion = "1.0.0"
}
if (-not $appVersion) {
    $appVersion = "1"
}

$tag = "v$displayVersion"
$releaseTitle = "MostaqlK $tag"

Write-Host "==========================================" -ForegroundColor Cyan
Write-Host " MostaqlK Release Automation" -ForegroundColor Cyan
Write-Host " Version:       $displayVersion (Build $appVersion)" -ForegroundColor Yellow
Write-Host " Target Tag:    $tag" -ForegroundColor Yellow
Write-Host " Architecture:  $Arch" -ForegroundColor Yellow
Write-Host "==========================================" -ForegroundColor Cyan

# 2. Check If Tag/Release Already Exists on GitHub
$shouldRelease = $true
$ghCli = Get-Command "gh" -ErrorAction SilentlyContinue

if ($ghCli -and (-not $Force)) {
    try {
        $null = & gh release view $tag 2>&1
        if ($LASTEXITCODE -eq 0) {
            Write-Host "GitHub release $tag already exists. Skipping publish." -ForegroundColor Yellow
            $shouldRelease = $false
        } else {
            Write-Host "GitHub release $tag does not exist yet. Proceeding with release." -ForegroundColor Green
            $shouldRelease = $true
        }
    } catch {
        Write-Host "Could not query GitHub releases ($($_.Exception.Message)). Assuming new release." -ForegroundColor Gray
        $shouldRelease = $true
    }
} elseif ($Force) {
    Write-Host "Force switch specified. Proceeding with release regardless of existing tags." -ForegroundColor Magenta
    $shouldRelease = $true
}

# Export outputs for GitHub Actions if running inside CI
if ($env:GITHUB_OUTPUT) {
    "version=$displayVersion" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
    "app_version=$appVersion" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
    "tag=$tag" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
    "release_title=$releaseTitle" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
    "should_release=$($shouldRelease.ToString().ToLower())" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
}

if ($CheckOnly) {
    Write-Host "CheckOnly switch provided. Exiting early." -ForegroundColor Cyan
    exit 0
}

if (-not $shouldRelease) {
    Write-Host "No release needed. Done." -ForegroundColor Green
    exit 0
}

# 3. Build Windows Portable Single-File Executable
$releaseScript = Join-Path $scriptDir "release-windows.ps1"
$releaseScriptArgs = @("-Type", "Portable", "-Arch", $Arch)
if ($ResetDatabase) {
    $releaseScriptArgs += "-ResetDatabase"
}
Write-Host "Running release script: $releaseScript $($releaseScriptArgs -join ' ')" -ForegroundColor Cyan
& $releaseScript @releaseScriptArgs

if ($LASTEXITCODE -ne 0) {
    Write-Error "Release build failed with exit code $LASTEXITCODE."
    exit $LASTEXITCODE
}

# 4. Locate and Verify Single-File Artifact
$target = "net10.0-windows10.0.19041.0"
$rid = "win-$Arch"
$publishDir = Join-Path $repoRoot "bin\Release\$target\$rid\publish"
$sourceExe = Join-Path $publishDir "MostaqlK.exe"

if (-not (Test-Path $sourceExe)) {
    Write-Error "Expected executable was not found at $sourceExe"
    exit 1
}

$exeSize = (Get-Item $sourceExe).Length
Write-Host "Found standalone executable: $sourceExe ($([Math]::Round($exeSize / 1MB, 2)) MB)" -ForegroundColor Green

# 5. Stage Release Artifacts
$stageDir = Join-Path $repoRoot "release_artifacts"
if (Test-Path $stageDir) {
    Remove-Item -Path $stageDir -Recurse -Force
}
New-Item -ItemType Directory -Path $stageDir -Force | Out-Null

$stagedExeName = "MostaqlK-$tag-windows-$Arch-portable.exe"
$stagedZipName = "MostaqlK-$tag-windows-$Arch-portable.zip"

$destExe = Join-Path $stageDir $stagedExeName
$destZip = Join-Path $stageDir $stagedZipName

Write-Host "Copying executable to $destExe..." -ForegroundColor Gray
Copy-Item -Path $sourceExe -Destination $destExe -Force

Write-Host "Compressing executable into $destZip..." -ForegroundColor Gray
# Compress the copied artifact to avoid file locks
if (Test-Path $destZip) {
    Remove-Item -Path $destZip -Force
}
Compress-Archive -Path $destExe -DestinationPath $destZip -Force

Write-Host "==========================================" -ForegroundColor Green
Write-Host " Release Artifacts Ready:" -ForegroundColor Green
Get-ChildItem -Path $stageDir | ForEach-Object {
    Write-Host "  - $($_.Name) ($([Math]::Round($_.Length / 1MB, 2)) MB)" -ForegroundColor Gray
}
Write-Host "==========================================" -ForegroundColor Green

if ($env:GITHUB_OUTPUT) {
    "artifact_exe=$destExe" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
    "artifact_zip=$destZip" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
    "artifact_dir=$stageDir" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
}
