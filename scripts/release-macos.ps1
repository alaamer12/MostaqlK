$projectName = "MostaqlK"
$target = "net10.0-maccatalyst"

Write-Host "Releasing MostaqlK for macOS (MacCatalyst)..." -ForegroundColor Cyan

# Note: macOS publish requires a Mac
dotnet publish $projectName.csproj -f $target -c Release

if ($LASTEXITCODE -eq 0) {
    Write-Host "macOS Release successful!" -ForegroundColor Green
} else {
    Write-Host "macOS Release failed!" -ForegroundColor Red
    exit $LASTEXITCODE
}
