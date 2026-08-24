param (
    [Parameter(Mandatory=$false)]
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

# Build for macOS (MacCatalyst)
Write-Host "Building MostaqlK for macOS ($Configuration)..." -ForegroundColor Cyan
dotnet build MostaqlK.csproj -f net10.0-maccatalyst -c $Configuration -p:TargetFrameworks=net10.0-maccatalyst
if ($LASTEXITCODE -eq 0) {
    Write-Host "macOS Build successful!" -ForegroundColor Green
} else {
    Write-Host "macOS Build failed!" -ForegroundColor Red
    exit $LASTEXITCODE
}
