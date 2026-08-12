# Clean the project
Write-Host "Cleaning MostaqlK..." -ForegroundColor Cyan
dotnet clean MostaqlK.csproj
dotnet clean MostaqlK.UITests/MostaqlK.UITests.csproj
Write-Host "Clean successful!" -ForegroundColor Green
