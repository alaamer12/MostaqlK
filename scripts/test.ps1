# Run the headless parser tests first - they are fast, offline and catch scraper/parser
# regressions long before the (slow, desktop-session-bound) UI tests would.
& "$PSScriptRoot\test-parser.ps1"
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

# Run UI tests
Write-Host "Running MostaqlK UI Tests..." -ForegroundColor Cyan
dotnet test MostaqlK.UITests/MostaqlK.UITests.csproj
if ($LASTEXITCODE -eq 0) {
    Write-Host "Tests passed!" -ForegroundColor Green
} else {
    Write-Host "Tests failed!" -ForegroundColor Red
    exit $LASTEXITCODE
}
