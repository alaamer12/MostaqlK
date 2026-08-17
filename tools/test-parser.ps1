# Run the headless parser test suite (tools/ParserTests).
# Fast, offline and MAUI-free - safe to run in CI, unlike the WinAppDriver UI tests.
#
#   .\scripts\test-parser.ps1                # run all fixture-based checks
#   .\scripts\test-parser.ps1 -Live <url>    # parse a real Mostaql project page and dump the result
param(
    [string]$Live
)

Write-Host "Running MostaqlK parser tests..." -ForegroundColor Cyan

if ($Live) {
    dotnet run --project tools\ParserTests -- --live $Live
} else {
    dotnet run --project tools\ParserTests
}

if ($LASTEXITCODE -eq 0) {
    Write-Host "Parser tests passed!" -ForegroundColor Green
} else {
    Write-Host "Parser tests failed!" -ForegroundColor Red
    exit $LASTEXITCODE
}
