# Build for Mobile (Android and iOS)
Write-Host "Building MostaqlK for Mobile..." -ForegroundColor Cyan

# Android
Write-Host "Building for Android..." -ForegroundColor Cyan
dotnet build MostaqlK.csproj -f net10.0-android -c Release -p:TargetFrameworks=net10.0-android
if ($LASTEXITCODE -ne 0) {
    Write-Host "Android Build failed!" -ForegroundColor Red
    exit $LASTEXITCODE
}

# iOS
Write-Host "Building for iOS..." -ForegroundColor Cyan
dotnet build MostaqlK.csproj -f net10.0-ios -c Release -p:TargetFrameworks=net10.0-ios
if ($LASTEXITCODE -ne 0) {
    Write-Host "iOS Build failed!" -ForegroundColor Red
    exit $LASTEXITCODE
}

Write-Host "Mobile builds successful!" -ForegroundColor Green
