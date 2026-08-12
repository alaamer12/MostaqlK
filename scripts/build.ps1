# Build the project
Write-Host "Building MostaqlK..." -ForegroundColor Cyan
dotnet build MostaqlK.csproj -f net10.0-windows10.0.19041.0
if ($LASTEXITCODE -eq 0) {
    Write-Host "Build successful!" -ForegroundColor Green
} else {
    Write-Host "Build failed!" -ForegroundColor Red
    exit $LASTEXITCODE
}
