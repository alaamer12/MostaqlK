# Deploy / Publish production build
Write-Host "Publishing production build for MostaqlK (Windows)..." -ForegroundColor Cyan
dotnet publish MostaqlK.csproj -f net10.0-windows10.0.19041.0 -c Release -p:PublishReadyToRun=true
if ($LASTEXITCODE -eq 0) {
    Write-Host "Publish successful! Check bin/Release/net10.0-windows10.0.19041.0/win10-x64/publish/" -ForegroundColor Green
} else {
    Write-Host "Publish failed!" -ForegroundColor Red
    exit $LASTEXITCODE
}
