param (
    [Parameter(Mandatory=$false)]
    [ValidateSet("Android", "iOS", "Both")]
    [string]$Platform = "Both"
)

$projectName = "MostaqlK"

Write-Host "Releasing MostaqlK for Mobile ($Platform)..." -ForegroundColor Cyan

if ($Platform -eq "Android" -or $Platform -eq "Both") {
    Write-Host "Publishing for Android..." -ForegroundColor Cyan
    dotnet publish $projectName.csproj -f net10.0-android -c Release
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Android Release failed!" -ForegroundColor Red
        if ($Platform -ne "Both") { exit $LASTEXITCODE }
    }
}

if ($Platform -eq "iOS" -or $Platform -eq "Both") {
    Write-Host "Publishing for iOS..." -ForegroundColor Cyan
    # Note: iOS publish requires a Mac or a connected Mac for signing
    dotnet publish $projectName.csproj -f net10.0-ios -c Release
    if ($LASTEXITCODE -ne 0) {
        Write-Host "iOS Release failed!" -ForegroundColor Red
        if ($Platform -ne "Both") { exit $LASTEXITCODE }
    }
}

Write-Host "Mobile release process completed." -ForegroundColor Green
