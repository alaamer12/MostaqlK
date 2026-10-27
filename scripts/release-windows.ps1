param (
    [Parameter(Mandatory=$false)]
    [ValidateSet("Portable", "Directory")]
    [string]$Type = "Directory",

    [Parameter(Mandatory=$false)]
    [ValidateSet("x64", "arm64", "x86")]
    [string]$Arch = "x64",

    [Parameter(Mandatory=$false)]
    [switch]$ResetDatabase,

    [Parameter(Mandatory=$false)]
    [ValidateSet("stable", "canary")]
    [string]$Channel = "stable"
)

$target = "net10.0-windows10.0.19041.0"
$projectName = "MostaqlK"
$rid = "win-$Arch"
$outputBase = "bin\Release\$target\$rid\publish"
$exePath = "$outputBase\$projectName.exe"

Write-Host "Releasing MostaqlK for Windows ($Type mode, Arch: $Arch, RID: $rid, channel: $Channel)..." -ForegroundColor Cyan

# Stop any running MostaqlK processes to avoid file locks on the database and output executable
$runningProcesses = Get-Process -Name $projectName -ErrorAction SilentlyContinue
if ($runningProcesses) {
    Write-Host "Stopping running $projectName process(es)..." -ForegroundColor Yellow
    $runningProcesses | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 500
}

# Reset state for clean release startup (skips database reset by default unless -ResetDatabase is specified).
# The reset scripts target the STABLE data root (%LocalAppData%\MostaqlK), so a canary release must never run them.
$toolsDir = Join-Path $PSScriptRoot "..\tools"
if ($Channel -eq "stable" -and (Test-Path $toolsDir)) {
    Write-Host "Running reset scripts for clean startup state..." -ForegroundColor Cyan
    $resetScripts = Get-ChildItem -Path $toolsDir -File | Where-Object { 
        ($_.Name -like "reset*.ps1" -or $_.Name -like "reset_*.ps1" -or $_.Name -like "reset-*.ps1") -and
        ($ResetDatabase -or $_.Name -notlike "*database*")
    }
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
    if (-not $ResetDatabase) {
        Write-Host "  Preserving local database (use -ResetDatabase to wipe database)." -ForegroundColor DarkGray
    }
} elseif ($Channel -eq "canary") {
    Write-Host "Skipping reset scripts for the canary channel (they target the stable data root)." -ForegroundColor DarkGray
}

$publishArgs = @(
    "publish", "$projectName.csproj",
    "-f", $target,
    "-c", "Release",
    "-r", $rid,
    "-p:TargetFrameworks=$target",
    "-p:Channel=$Channel",
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

    # WinUI's Microsoft.ui.xaml.dll loads its own strings from <its own folder>\<lang>\
    # Microsoft.ui.xaml.dll.mui and hard-fails ~4s into the first layout pass (WinUI.
    # UnhandledException 0x80004002 -> XAML fail-fast 0xc000027b) when the en-US fallback is missing.
    # A layout without it is not runnable, so that must be a build error here, not a crash on the
    # user's machine — and the two release shapes carry the folders in two different places:
    #   Directory: loose next to the exe (verified: deleting them via a locale-prune target killed
    #              every published build ~4s after launch while `dotnet build` outputs survived).
    #   Portable:  inside the single-file bundle, which the apphost extracts whole (verified:
    #              %LocalAppData%\MostaqlK.Canary\bundle-cache\... got all 89 locale MUI folders,
    #              so the cleanup above removing loose dirs is harmless for this shape).
    $muiName = "Microsoft.ui.xaml.dll.mui"
    if ($Type -eq "Portable") {
        & findstr /M /C:"$muiName" "$exePath" > $null 2>&1
        if ($LASTEXITCODE -ne 0) {
            Write-Host "Release failed: the single-file bundle does not carry the WinUI MUI fallback ($muiName)." -ForegroundColor Red
            exit 1
        }
    } else {
        $winUiMui = Join-Path $outputBase "en-US\$muiName"
        if (-not (Test-Path $winUiMui)) {
            Write-Host "Release failed: publish layout is missing the WinUI MUI fallback: $winUiMui" -ForegroundColor Red
            exit 1
        }
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
