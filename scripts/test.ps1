# Run UI tests
Write-Host "Running MostaqlK UI Tests..." -ForegroundColor Cyan
dotnet test MostaqlK.UITests/MostaqlK.UITests.csproj
if ($LASTEXITCODE -eq 0) {
    Write-Host "Tests passed!" -ForegroundColor Green
} else {
    Write-Host "Tests failed!" -ForegroundColor Red
    exit $LASTEXITCODE
}
