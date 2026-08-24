param(
    [string]$PreferencesPath = (Join-Path $env:LOCALAPPDATA 'MostaqlK\Settings\preferences.dat'),
    [switch]$ConfirmReset
)

$ErrorActionPreference = 'Stop'

if (-not $ConfirmReset) {
    throw 'Re-run with -ConfirmReset after closing MostaqlK.'
}

if (-not (Test-Path -LiteralPath $PreferencesPath -PathType Leaf)) {
    throw "Preferences file was not found: $PreferencesPath"
}

# The unpackaged Windows build of MAUI's Preferences API (see Services/CloseBehaviorService.cs)
# stores every key/value pair as flat JSON under a single "" container in this one file - there
# is no per-key file to just delete, so the two "remember close behavior" keys have to be removed
# from the parsed JSON and the file rewritten. ConvertFrom-Json can't round-trip an empty-named
# ("") property under Windows PowerShell 5.1 (the shell targeted by this repo's other tools
# scripts), so JavaScriptSerializer (returns a Hashtable, tolerates the "" key) is used instead.
Add-Type -AssemblyName System.Web.Extensions
$serializer = New-Object System.Web.Script.Serialization.JavaScriptSerializer
$raw = Get-Content -LiteralPath $PreferencesPath -Raw
$json = $serializer.DeserializeObject($raw)

$container = $json['']
if ($null -eq $container) {
    Write-Host 'No preferences container found; nothing to reset.'
    exit 0
}

$keysToRemove = @('close_behavior_remembered', 'close_behavior_action')
$removedAny = $false
foreach ($key in $keysToRemove) {
    if ($container.ContainsKey($key)) {
        [void]$container.Remove($key)
        Write-Host "Removed preference: $key"
        $removedAny = $true
    }
}

if (-not $removedAny) {
    Write-Host 'The "remember close behavior" preference was not set; nothing to reset.'
    exit 0
}

$serializer.Serialize($json) | Set-Content -LiteralPath $PreferencesPath -Encoding utf8 -NoNewline

Write-Host 'Close-behavior preference reset. The next X-button click will show the confirmation dialog again.'
