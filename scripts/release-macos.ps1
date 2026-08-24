$projectName = "MostaqlK"
$target = "net10.0-maccatalyst"

Write-Host "Releasing MostaqlK for macOS (MacCatalyst)..." -ForegroundColor Cyan

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

# Note: macOS publish requires a Mac
dotnet publish $projectName.csproj -f $target -c Release

if ($LASTEXITCODE -eq 0) {
    Write-Host "macOS Release successful!" -ForegroundColor Green
} else {
    Write-Host "macOS Release failed!" -ForegroundColor Red
    exit $LASTEXITCODE
}
