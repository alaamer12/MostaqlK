# Run the application with Hot Reload (dotnet watch)
param (
    [Parameter(Mandatory=$false)]
    [ValidateSet("Windows", "Android", "iOS")]
    [string]$Platform = "Android",

    [Parameter(Mandatory=$false)]
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug"
)

# Enable polling file watcher to prevent Windows kernel buffer overflow during Android Java/DEX builds
$env:DOTNET_USE_POLLING_FILE_WATCHER = "1"

$tfm = switch ($Platform) {
    "Windows" { "net10.0-windows10.0.19041.0" }
    "Android" { "net10.0-android" }
    "iOS"     { "net10.0-ios" }
}

Write-Host "Watching MostaqlK for $Platform ($Configuration) with Hot Reload (Polling File Watcher enabled)..." -ForegroundColor Cyan
dotnet watch --project MostaqlK.csproj -f $tfm -c $Configuration
