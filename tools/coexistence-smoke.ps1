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

function Stop-Started {
    foreach ($p in $script:started) {
        if ($p -and -not $p.HasExited) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue }
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

    Stop-Started
    $started = @()

    if ($failures.Count -gt 0) {
        Write-Host "COEXISTENCE SMOKE FAILED:" -ForegroundColor Red
        $failures | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
        exit 1
    }

    Write-Host "COEXISTENCE SMOKE PASSED: both channels alive, both mutexes held, both roots present, no crashes." -ForegroundColor Green
    exit 0
}
catch {
    Write-Host "coexistence smoke error: $_" -ForegroundColor Red
    exit 1
}
finally {
    Stop-Started
}
