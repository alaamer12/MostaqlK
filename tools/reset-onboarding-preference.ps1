param(
    [string]$PreferencesPath = (Join-Path $env:LOCALAPPDATA 'User Name\com.companyname.mostaqlk\Settings\preferences.dat'),
    [switch]$ConfirmReset
)

$ErrorActionPreference = 'Stop'

if (-not $ConfirmReset) {
    throw 'Re-run with -ConfirmReset after closing MostaqlK.'
}

if (-not (Test-Path -LiteralPath $PreferencesPath -PathType Leaf)) {
    Write-Host "Preferences file was not found; onboarding is already reset: $PreferencesPath"
    exit 0
}

Add-Type -AssemblyName System.Web.Extensions
$serializer = New-Object System.Web.Script.Serialization.JavaScriptSerializer
$raw = Get-Content -LiteralPath $PreferencesPath -Raw
$json = $serializer.DeserializeObject($raw)

$container = $json['']
if ($null -eq $container) {
    Write-Host 'No preferences container found; onboarding is already reset.'
    exit 0
}

$key = 'onboarding_completed'
if (-not $container.ContainsKey($key)) {
    Write-Host 'The onboarding completion preference was not set; nothing to reset.'
    exit 0
}

[void]$container.Remove($key)
$serializer.Serialize($json) | Set-Content -LiteralPath $PreferencesPath -Encoding utf8 -NoNewline
Write-Host 'Onboarding completion preference reset. The onboarding window will appear on the next launch.'