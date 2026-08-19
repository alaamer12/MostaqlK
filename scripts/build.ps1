# Build the project
param (
    [Parameter(Mandatory=$false)]
    [ValidateSet("Windows", "macOS", "Android", "iOS", "Linux", "All")]
    [string]$Platform = "Windows",

    [Parameter(Mandatory=$false)]
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug"
)

switch ($Platform) {
    "Windows" {
        Write-Host "Building MostaqlK for Windows ($Configuration)..." -ForegroundColor Cyan
        dotnet build MostaqlK.csproj -f net10.0-windows10.0.19041.0 -c $Configuration
    }
    "macOS" {
        & "$PSScriptRoot\build-macos.ps1"
    }
    "Android" {
        Write-Host "Building MostaqlK for Android ($Configuration)..." -ForegroundColor Cyan
        dotnet build MostaqlK.csproj -f net10.0-android -c $Configuration
    }
    "iOS" {
        Write-Host "Building MostaqlK for iOS ($Configuration)..." -ForegroundColor Cyan
        dotnet build MostaqlK.csproj -f net10.0-ios -c $Configuration
    }
    "Linux" {
        & "$PSScriptRoot\build-linux.ps1"
    }
    "All" {
        Write-Host "Building MostaqlK for All Platforms ($Configuration)..." -ForegroundColor Cyan
        dotnet build MostaqlK.csproj -c $Configuration
    }
}

if ($LASTEXITCODE -eq 0) {
    Write-Host "Build successful!" -ForegroundColor Green
} else {
    Write-Host "Build failed!" -ForegroundColor Red
    exit $LASTEXITCODE
}
