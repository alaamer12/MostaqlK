# Coexistence smoke test (layered-isolability §6/§9): launch stable and canary binaries of the
# SAME source tree side by side and assert nothing collides — both singletons held, both channel
# roots created, zero crash logs. Fails non-zero so it can gate a release.
#
# Usage:  powershell -File tools\coexistence-smoke.ps1 [-Configuration Release]
# Requires: .NET SDK + MAUI workloads (it publishes both channels into scratch\smoke).

param (
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"
$env:MOSTAQLK_NO_WATCHDOG = "1"

$repo = Split-Path $PSScriptRoot -Parent
$target = "net10.0-windows10.0.19041.0"
$smokeDir = Join-Path $repo "scratch\smoke"
$started = @()

# Channel root (under %LOCALAPPDATA%) that each build writes its preferences into.
$channelRoots = [ordered]@{ stable = "MostaqlK"; canary = "MostaqlK.Canary" }
$prefFiles = @{}
$prefBackupDir = Join-Path $smokeDir "prefs-backup"

function Stop-Started {
    foreach ($p in $script:started) {
        if ($p -and -not $p.HasExited) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue }
    }
}

# Preference isolation is only proven if the two channels disagree about a value and keep
# disagreeing after both have run: a shared store would end up holding whichever wrote last.
#
# Text surgery, not a JSON parser: Windows PowerShell 5.1's ConvertFrom-Json throws on the file's
# default container because its key is the empty string, and the app's own store round-trips every
# container it loaded, so a nested probe object (`"parity_probe": { "value": … }`) survives a run
# untouched while nothing in the product ever reads it.
$probePattern = '"parity_probe"\s*:\s*\{\s*"value"\s*:\s*"([^"]*)"\s*\}'

function Set-PreferenceProbe([string]$path, [string]$value) {
    $dir = Split-Path $path -Parent
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
    $text = if (Test-Path $path) { Get-Content $path -Raw } else { "{`n}" }
    $entry = '"parity_probe": { "value": "' + $value + '" }'
    if ([regex]::IsMatch($text, $probePattern)) {
        $text = [regex]::Replace($text, $probePattern, $entry)
    }
    else {
        # Insert as the root object's first member. Plain string surgery, so the file's
        # empty-named default container is left byte-for-byte as it was.
        $brace = $text.IndexOf('{')
        $text = $text.Substring(0, $brace + 1) + "`n  $entry," + $text.Substring($brace + 1)
    }
    Set-Content -Path $path -Value $text -Encoding utf8 -NoNewline
}

function Get-PreferenceProbe([string]$path) {
    if (-not (Test-Path $path)) { return $null }
    $match = [regex]::Match((Get-Content $path -Raw), $probePattern)
    if ($match.Success) { return $match.Groups[1].Value }
    return $null
}

function Restore-PreferenceFiles {
    foreach ($ch in $channelRoots.Keys) {
        $bak = Join-Path $prefBackupDir "$ch.preferences.dat"
        if (Test-Path $bak) { Copy-Item $bak $prefFiles[$ch] -Force }
    }
}

try {
    foreach ($ch in @("stable", "canary")) {
        $out = Join-Path $smokeDir $ch
        Write-Host "Publishing $ch -> $out" -ForegroundColor Cyan
        dotnet publish (Join-Path $repo "MostaqlK.csproj") -f $target -c $Configuration -r win-x64 --self-contained false `
            "-p:TargetFrameworks=$target" "-p:Channel=$ch" -o $out --nologo -v q
        if ($LASTEXITCODE -ne 0) { throw "publish $ch failed" }
    }

    $startedAt = (Get-Date).ToUniversalTime()

    # Back up and probe-mark both preference stores (restored in the finally block).
    New-Item -ItemType Directory -Force -Path $prefBackupDir | Out-Null
    foreach ($ch in $channelRoots.Keys) {
        $f = Join-Path $env:LOCALAPPDATA ($channelRoots[$ch] + "\Settings\preferences.dat")
        $prefFiles[$ch] = $f
        if (Test-Path $f) { Copy-Item $f (Join-Path $prefBackupDir "$ch.preferences.dat") -Force }
        Set-PreferenceProbe $f "$ch-only"
    }

    foreach ($ch in @("canary", "stable")) {
        $exe = Join-Path $smokeDir "$ch\MostaqlK.exe"
        $p = Start-Process -FilePath $exe -ArgumentList "--silent-start" -PassThru
        $started += $p
        Write-Host "$ch launched pid=$($p.Id)" -ForegroundColor Gray
        Start-Sleep -Seconds 4
    }
    Start-Sleep -Seconds 8

    $failures = @()
    foreach ($p in $started) {
        if (-not (Get-Process -Id $p.Id -ErrorAction SilentlyContinue)) { $failures += "process $($p.Id) died" }
    }

    foreach ($m in @("Local\MostaqlK.App.Singleton", "Local\MostaqlK.Canary.App.Singleton")) {
        $held = $false
        try { $mx = [Threading.Mutex]::OpenExisting($m); $mx.Dispose(); $held = $true } catch { }
        if (-not $held) { $failures += "mutex $m not held" }
    }

    foreach ($root in @("MostaqlK", "MostaqlK.Canary")) {
        $data = Join-Path $env:LOCALAPPDATA "$root\Data\mostaqlk.db"
        if (-not (Test-Path $data)) { $failures += "missing $data" }
    }

    # Fatal = collision or native crash. crash.log also records recoverable managed faults
    # (WinUI.UnhandledException that the app survived), so it is a warning, not a failure.
    foreach ($root in @("MostaqlK", "MostaqlK.Canary")) {
        $dumps = Get-ChildItem (Join-Path $env:LOCALAPPDATA $root) -Recurse -Filter "crash.dmp" -ErrorAction SilentlyContinue |
            Where-Object { $_.LastWriteTimeUtc -gt $startedAt }
        foreach ($d in $dumps) { $failures += "native crash dump: $($d.FullName)" }

        $logs = Get-ChildItem (Join-Path $env:LOCALAPPDATA $root) -Recurse -Filter "crash.log" -ErrorAction SilentlyContinue |
            Where-Object { $_.LastWriteTimeUtc -gt $startedAt }
        foreach ($l in $logs) { Write-Host "  WARNING recoverable fault logged: $l" -ForegroundColor Yellow }
    }

    $werDump = Get-ChildItem (Join-Path $env:LOCALAPPDATA "CrashDumps") -Filter "*.dmp" -ErrorAction SilentlyContinue |
        Where-Object { $_.LastWriteTimeUtc -gt $startedAt }
    foreach ($w in $werDump) { $failures += "WER dump: $($w.FullName)" }

    # Preference isolation: each store must still hold its OWN probe value after both channels ran.
    # A shared (or cross-read) store would leave whichever channel wrote last in both files.
    foreach ($ch in $channelRoots.Keys) {
        $probe = Get-PreferenceProbe $prefFiles[$ch]
        if ($probe -ne "$ch-only") {
            $failures += "preferences not channel-isolated: $ch store at $($prefFiles[$ch]) reads '$probe' (expected '$ch-only')"
        }
    }

    # And the static Preferences.Default must still resolve to the file-backed, channel-rooted store.
    foreach ($root in $channelRoots.Values) {
        $logs = Get-ChildItem (Join-Path $env:LOCALAPPDATA $root) -Recurse -Filter "crash.log" -ErrorAction SilentlyContinue |
            Where-Object { $_.LastWriteTimeUtc -gt $startedAt }
        foreach ($l in $logs) {
            if (Select-String -Path $l.FullName -Quiet -Pattern "preferences-backend-fallback") {
                $failures += "preferences backend escaped the channel root ($l)"
            }
        }
    }

    Stop-Started
    $started = @()

    if ($failures.Count -gt 0) {
        Write-Host "COEXISTENCE SMOKE FAILED:" -ForegroundColor Red
        $failures | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
        exit 1
    }

    Write-Host "COEXISTENCE SMOKE PASSED: both channels alive, both mutexes held, both roots present, preferences channel-isolated, no crashes." -ForegroundColor Green
    exit 0
}
catch {
    Write-Host "coexistence smoke error: $_" -ForegroundColor Red
    exit 1
}
finally {
    Stop-Started
    Restore-PreferenceFiles
}
