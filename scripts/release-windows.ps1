param (
    [Parameter(Mandatory=$false)]
    [ValidateSet("Portable", "Directory")]
    [string]$Type = "Directory",

    [Parameter(Mandatory=$false)]
    [ValidateSet("x64", "arm64", "x86")]
    [string]$Arch = "x64"
)

$target = "net10.0-windows10.0.19041.0"
$projectName = "MostaqlK"
$rid = "win-$Arch"
$outputBase = "bin\Release\$target\$rid\publish"
$exePath = "$outputBase\$projectName.exe"

Write-Host "Releasing MostaqlK for Windows ($Type mode, Arch: $Arch, RID: $rid)..." -ForegroundColor Cyan

# Stop any running MostaqlK processes to avoid file locks on the database and output executable
$runningProcesses = Get-Process -Name $projectName -ErrorAction SilentlyContinue
if ($runningProcesses) {
    Write-Host "Stopping running $projectName process(es)..." -ForegroundColor Yellow
    $runningProcesses | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 500
}

# Reset state for clean release startup
$toolsDir = Join-Path $PSScriptRoot "..\tools"
if (Test-Path $toolsDir) {
    Write-Host "Running reset scripts for clean startup state..." -ForegroundColor Cyan
    $resetScripts = Get-ChildItem -Path $toolsDir -File | Where-Object { $_.Name -like "reset*.ps1" -or $_.Name -like "reset_*.ps1" -or $_.Name -like "reset-*.ps1" }
    foreach ($script in $resetScripts) {
        try {
            Write-Host "  Invoking $($script.Name)..." -ForegroundColor Gray
            $cmd = Get-Command $script.FullName -ErrorAction SilentlyContinue
            if ($cmd -and $cmd.Parameters.ContainsKey('ConfirmReset')) {
                & $script.FullName -ConfirmReset -ErrorAction Stop
            } else {
                & $script.FullName -ErrorAction Stop
            }
        } catch {
            Write-Host "  $($script.Name) skipped or completed with message: $($_.Exception.Message)" -ForegroundColor DarkGray
        }
    }
}

$publishArgs = @(
    "publish", "$projectName.csproj",
    "-f", $target,
    "-c", "Release",
    "-r", $rid,
    "-p:TargetFrameworks=$target",
    "-p:PublishReadyToRun=true",
    "-p:SatelliteResourceLanguages=ar"
)

if ($Type -eq "Portable") {
    Write-Host "Configuring for single-file portable executable..." -ForegroundColor Cyan
    $publishArgs += "-p:PublishSingleFile=true"
    $publishArgs += "-p:SelfContained=true"
    $publishArgs += "-p:IncludeNativeLibrariesForSelfExtract=true"
    $publishArgs += "-p:EnableCompressionInSingleFile=true"
    $publishArgs += "-p:DebugType=None"
    $publishArgs += "-p:DebugSymbols=false"
} else {
    Write-Host "Configuring for normal directory export..." -ForegroundColor Cyan
    $publishArgs += "-p:PublishSingleFile=false"
    $publishArgs += "-p:SelfContained=true"
}

dotnet @publishArgs

if ($LASTEXITCODE -eq 0) {
    if ($Type -eq "Portable") {
        Write-Host "Cleaning up portable publish directory..." -ForegroundColor Cyan
        # Remove any leftover .pdb files
        Get-ChildItem -Path $outputBase -Filter "*.pdb" -Recurse -File -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue
        # Remove all directories/subdirectories inside output directory
        Get-ChildItem -Path $outputBase -Directory -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
        # Remove any non-exe files if any remain
        Get-ChildItem -Path $outputBase -File -ErrorAction SilentlyContinue | Where-Object { $_.Name -ne "$projectName.exe" } | Remove-Item -Force -ErrorAction SilentlyContinue
    }

    Write-Host "Release successful!" -ForegroundColor Green
    Write-Host "Output located at: $outputBase" -ForegroundColor Gray
    if (Test-Path $exePath) {
        $fileInfo = Get-Item $exePath
        $sizeMB = [math]::Round($fileInfo.Length / 1MB, 2)
        Write-Host "Executable generated: $($fileInfo.FullName) ($sizeMB MB)" -ForegroundColor Green
    } else {
        Write-Warning "Executable not found at expected path: $exePath"
    }
} else {
    Write-Host "Release failed!" -ForegroundColor Red
    exit $LASTEXITCODE
}
