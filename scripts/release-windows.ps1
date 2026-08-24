param (
    [Parameter(Mandatory=$false)]
    [ValidateSet("Portable", "Directory")]
    [string]$Type = "Directory",

    [Parameter(Mandatory=$false)]
    [ValidateSet("x64", "arm64", "x86")]
    [string]$Arch = "x64"
)

$target = "net10.0-windows10.0.19041.0"
$projectName = "MostaqlK"
$rid = "win-$Arch"
$outputBase = "bin\Release\$target\$rid\publish"
$exePath = "$outputBase\$projectName.exe"

Write-Host "Releasing MostaqlK for Windows ($Type mode, Arch: $Arch, RID: $rid)..." -ForegroundColor Cyan

$publishArgs = @(
    "publish", "$projectName.csproj",
    "-f", $target,
    "-c", "Release",
    "-r", $rid,
    "-p:TargetFrameworks=$target",
    "-p:PublishReadyToRun=true",
    "-p:SatelliteResourceLanguages=ar"
)

if ($Type -eq "Portable") {
    Write-Host "Configuring for single-file portable executable..." -ForegroundColor Cyan
    $publishArgs += "-p:PublishSingleFile=true"
    $publishArgs += "-p:SelfContained=true"
    $publishArgs += "-p:IncludeNativeLibrariesForSelfExtract=true"
    $publishArgs += "-p:EnableCompressionInSingleFile=true"
    $publishArgs += "-p:DebugType=None"
    $publishArgs += "-p:DebugSymbols=false"
} else {
    Write-Host "Configuring for normal directory export..." -ForegroundColor Cyan
    $publishArgs += "-p:PublishSingleFile=false"
    $publishArgs += "-p:SelfContained=true"
}

dotnet @publishArgs

if ($LASTEXITCODE -eq 0) {
    if ($Type -eq "Portable") {
        Write-Host "Cleaning up portable publish directory..." -ForegroundColor Cyan
        # Remove any leftover .pdb files
        Get-ChildItem -Path $outputBase -Filter "*.pdb" -Recurse -File -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue
        # Remove all directories/subdirectories inside output directory
        Get-ChildItem -Path $outputBase -Directory -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
        # Remove any non-exe files if any remain
        Get-ChildItem -Path $outputBase -File -ErrorAction SilentlyContinue | Where-Object { $_.Name -ne "$projectName.exe" } | Remove-Item -Force -ErrorAction SilentlyContinue
    }

    Write-Host "Release successful!" -ForegroundColor Green
    Write-Host "Output located at: $outputBase" -ForegroundColor Gray
    if (Test-Path $exePath) {
        $fileInfo = Get-Item $exePath
        $sizeMB = [math]::Round($fileInfo.Length / 1MB, 2)
        Write-Host "Executable generated: $($fileInfo.FullName) ($sizeMB MB)" -ForegroundColor Green
    } else {
        Write-Warning "Executable not found at expected path: $exePath"
    }
} else {
    Write-Host "Release failed!" -ForegroundColor Red
    exit $LASTEXITCODE
}
