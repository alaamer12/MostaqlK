param (
    [Parameter(Mandatory=$false)]
    [switch]$ResetDatabase
)

$projectName = "MostaqlK"
$target = "net10.0-maccatalyst"

Write-Host "Releasing MostaqlK for macOS (MacCatalyst)..." -ForegroundColor Cyan

# Reset state for clean release startup (skips database reset by default unless -ResetDatabase is specified)
$toolsDir = Join-Path $PSScriptRoot "..\tools"
if (Test-Path $toolsDir) {
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
}

# Note: macOS publish requires a Mac
dotnet publish $projectName.csproj -f $target -c Release -p:TargetFrameworks=$target

if ($LASTEXITCODE -eq 0) {
    Write-Host "macOS Release successful!" -ForegroundColor Green
} else {
    Write-Host "macOS Release failed!" -ForegroundColor Red
    exit $LASTEXITCODE
}
