param (
    [Parameter(Mandatory=$false)]
    [ValidateSet("Android", "iOS", "Both")]
    [string]$Platform = "Both",

    [Parameter(Mandatory=$false)]
    [switch]$ResetDatabase
)

$projectName = "MostaqlK"

Write-Host "Releasing MostaqlK for Mobile ($Platform)..." -ForegroundColor Cyan

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

if ($Platform -eq "Android" -or $Platform -eq "Both") {
    Write-Host "Publishing for Android..." -ForegroundColor Cyan
    dotnet publish $projectName.csproj -f net10.0-android -c Release -p:TargetFrameworks=net10.0-android
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Android Release failed!" -ForegroundColor Red
        if ($Platform -ne "Both") { exit $LASTEXITCODE }
    }
}

if ($Platform -eq "iOS" -or $Platform -eq "Both") {
    Write-Host "Publishing for iOS..." -ForegroundColor Cyan
    # Note: iOS publish requires a Mac or a connected Mac for signing
    dotnet publish $projectName.csproj -f net10.0-ios -c Release -p:TargetFrameworks=net10.0-ios
    if ($LASTEXITCODE -ne 0) {
        Write-Host "iOS Release failed!" -ForegroundColor Red
        if ($Platform -ne "Both") { exit $LASTEXITCODE }
    }
}

Write-Host "Mobile release process completed." -ForegroundColor Green
