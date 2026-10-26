param([switch]$Fix)

# Repo quality runner: package freshness, C# format/lint, markdown lint.
# Use -Fix to apply formatting/lint edits instead of only verifying.

$ErrorActionPreference = "Continue"

Write-Host "==> Package freshness (dotnet-outdated)"
dotnet dotnet-outdated MostaqlK.csproj

Write-Host "`n==> C# whitespace formatting (dotnet format whitespace)"
if ($Fix) {
    dotnet format whitespace . --folder --exclude bin --exclude obj --exclude scratch --exclude tools --exclude MostaqlK.UITests --exclude _site --exclude api --exclude .repertoire
} else {
    dotnet format whitespace . --folder --verify-no-changes --exclude bin --exclude obj --exclude scratch --exclude tools --exclude MostaqlK.UITests --exclude _site --exclude api --exclude .repertoire
}

# C# style lint (dotnet format style, warn+ per .editorconfig).
# NOTE: IDE0051/IDE0052 (remove unused) are pinned to `suggestion` in .editorconfig on
# purpose — this project is loaded for one TFM at a time, so platform-exclusive partials'
# members appear unused and mass-removal breaks other-platform builds.

Write-Host "`n==> C# style lint (dotnet format style, warn+ per .editorconfig)"
if ($Fix) {
    dotnet format style MostaqlK.csproj --severity warn
} else {
    dotnet format style MostaqlK.csproj --severity warn --verify-no-changes
}

Write-Host "`n==> Markdown lint (markdownlint-cli2)"
if ($Fix) {
    bunx markdownlint-cli2 --fix
} else {
    bunx markdownlint-cli2
}
