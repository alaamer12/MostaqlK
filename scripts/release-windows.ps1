param (
    [Parameter(Mandatory=$false)]
    [ValidateSet("Portable", "Directory")]
    [string]$Type = "Directory"
)

$target = "net10.0-windows10.0.19041.0"
$projectName = "MostaqlK"
$outputBase = "bin\Release\$target\publish"

Write-Host "Releasing MostaqlK for Windows ($Type mode)..." -ForegroundColor Cyan

$publishArgs = @(
    "publish", "$projectName.csproj",
    "-f", $target,
    "-c", "Release",
    "-p:PublishReadyToRun=true",
    "-p:SatelliteResourceLanguages=ar"
)

if ($Type -eq "Portable") {
    Write-Host "Configuring for single-file portable executable..." -ForegroundColor Cyan
    $publishArgs += "-p:PublishSingleFile=true"
    $publishArgs += "-p:SelfContained=true"
    $publishArgs += "-p:IncludeNativeLibrariesForSelfExtract=true"
} else {
    Write-Host "Configuring for normal directory export..." -ForegroundColor Cyan
    $publishArgs += "-p:PublishSingleFile=false"
    $publishArgs += "-p:SelfContained=true"
}

dotnet @publishArgs

if ($LASTEXITCODE -eq 0) {
    Write-Host "Release successful!" -ForegroundColor Green
    Write-Host "Output located at: $outputBase" -ForegroundColor Gray
} else {
    Write-Host "Release failed!" -ForegroundColor Red
    exit $LASTEXITCODE
}
