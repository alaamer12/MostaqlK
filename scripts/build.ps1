# Build the project
param (
    [Parameter(Mandatory=$false)]
    [ValidateSet("Windows", "macOS", "Android", "iOS", "Linux", "All")]
    [string]$Platform = "Windows"
)

switch ($Platform) {
    "Windows" {
        Write-Host "Building MostaqlK for Windows..." -ForegroundColor Cyan
        dotnet build MostaqlK.csproj -f net10.0-windows10.0.19041.0
    }
    "macOS" {
        & "$PSScriptRoot\build-macos.ps1"
    }
    "Android" {
        Write-Host "Building MostaqlK for Android..." -ForegroundColor Cyan
        dotnet build MostaqlK.csproj -f net10.0-android -c Release
    }
    "iOS" {
        Write-Host "Building MostaqlK for iOS..." -ForegroundColor Cyan
        dotnet build MostaqlK.csproj -f net10.0-ios -c Release
    }
    "Linux" {
        & "$PSScriptRoot\build-linux.ps1"
    }
    "All" {
        Write-Host "Building MostaqlK for All Platforms..." -ForegroundColor Cyan
        dotnet build MostaqlK.csproj
    }
}

if ($LASTEXITCODE -eq 0) {
    Write-Host "Build successful!" -ForegroundColor Green
} else {
    Write-Host "Build failed!" -ForegroundColor Red
    exit $LASTEXITCODE
}
