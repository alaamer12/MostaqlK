# Build for Linux
# Note: .NET MAUI does not officially support Linux yet.
# Community-supported backends like Maui.Linux or others might be required.
Write-Host "Building MostaqlK for Linux..." -ForegroundColor Cyan
Write-Host "Warning: .NET MAUI has no official Linux support. Attempting a generic build..." -ForegroundColor Yellow
dotnet build MostaqlK.csproj -c Release
if ($LASTEXITCODE -eq 0) {
    Write-Host "Linux Build (generic) successful!" -ForegroundColor Green
} else {
    Write-Host "Linux Build failed! (Official Linux support is missing in MAUI)" -ForegroundColor Red
    exit $LASTEXITCODE
}
