# Build for macOS (MacCatalyst)
Write-Host "Building MostaqlK for macOS..." -ForegroundColor Cyan
dotnet build MostaqlK.csproj -f net10.0-maccatalyst -c Release
if ($LASTEXITCODE -eq 0) {
    Write-Host "macOS Build successful!" -ForegroundColor Green
} else {
    Write-Host "macOS Build failed!" -ForegroundColor Red
    exit $LASTEXITCODE
}
