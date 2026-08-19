# Run the application
param (
    [Parameter(Mandatory=$false)]
    [ValidateSet("Windows", "Android", "iOS")]
    [string]$Platform = "Windows",

    [Parameter(Mandatory=$false)]
    [switch]$Watch,

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

Write-Host "Running MostaqlK for $Platform ($Configuration)..." -ForegroundColor Cyan

if ($Watch) {
    Write-Host "Starting dotnet watch with Hot Reload (Polling File Watcher enabled)..." -ForegroundColor Yellow
    dotnet watch --project MostaqlK.csproj -f $tfm -c $Configuration
} else {
    if ($Platform -eq "Android") {
        dotnet build MostaqlK.csproj -t:Run -f $tfm -c $Configuration
    } else {
        dotnet run --project MostaqlK.csproj -f $tfm -c $Configuration
    }
}
