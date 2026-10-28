# Coexistence smoke test (layered-isolability §6/§9): launch stable and canary binaries of the
# SAME source tree side by side and assert nothing collides — both singletons held, both channel
# roots created, zero crash logs. Fails non-zero so it can gate a release.
#
# Usage:  powershell -File tools\coexistence-smoke.ps1 [-Configuration Release]
# Requires: .NET SDK + MAUI workloads (it publishes both channels into scratch\smoke).

param (
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",

    # How long both instances must stay up. 4s was enough to catch the published-layout XAML
    # fail-fast, and nothing longer; a crash that arrives later (first toast, first poll cycle,
    # a long-idle path) walked past it. The soak is now explicit and repeatable.
    [int]$SoakSeconds = 30,

    # Re-run the checks against layouts that were already published (iteration without paying
    # ~2 minutes of MSBuild per attempt).
    [switch]$SkipPublish
)

$ErrorActionPreference = "Stop"
$env:MOSTAQLK_NO_WATCHDOG = "1"
# Ask both instances to log the first chance of every WinRT interop failure. The fatal one arrives
# at the UI thread's unhandled handler as a bare InvalidCastException with an EMPTY stack, which is
# useless for diagnosis; first-chance sees it where it was thrown.
$env:MOSTAQLK_DIAG_COM = "1"

$repo = Split-Path $PSScriptRoot -Parent
$target = "net10.0-windows10.0.19041.0"
$smokeDir = Join-Path $repo "scratch\smoke"
$started = @()
$launches = @()

# Channel root (under %LOCALAPPDATA%) that each build writes its preferences into.
$channelRoots = [ordered]@{ stable = "MostaqlK"; canary = "MostaqlK.Canary" }
$prefFiles = @{}
$prefBackupDir = Join-Path $smokeDir "prefs-backup"

function Stop-Started {
    foreach ($p in $script:started) {
        if ($p -and -not $p.HasExited) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue }
    }
}

# "Is the instance still running?" cannot be answered by pid alone: Windows reuses pids, so a
# crashed instance whose number was handed to an unrelated process passes a bare Get-Process -Id
# check and the smoke reports coexistence it never observed. The identity of "our instance" is the
# executable path plus a start time after this run began — both must agree.
function Get-LivenessIssue($process, [string]$exe) {
    $proc = $null
    try { $proc = Get-Process -Id $process.Id -ErrorAction Stop } catch { return "pid $($process.Id) is gone" }

    $path = $null
    try { $path = $proc.Path } catch { $path = $null }
    if ($path -and -not [string]::Equals($path.TrimEnd('\'), $exe.TrimEnd('\'), [StringComparison]::OrdinalIgnoreCase)) {
        return "pid $($process.Id) now belongs to another process ($path)"
    }

    try {
        if ($proc.StartTime -lt $script:startedAt.AddSeconds(-10).ToLocalTime()) {
            return "pid $($process.Id) started before this run ($($proc.StartTime)) - pid recycled or wrong instance"
        }
    } catch { }

    return $null
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
        if ($SkipPublish) {
            if (-not (Test-Path (Join-Path $out "MostaqlK.exe"))) { throw "-SkipPublish but no layout at $out" }
            Write-Host "Skipping publish for $ch (-SkipPublish)" -ForegroundColor DarkGray
            continue
        }
        Write-Host "Publishing $ch -> $out" -ForegroundColor Cyan
        # Same shape as scripts/release-windows.ps1's directory export: self-contained, single-file
        # OFF, Arabic .NET satellites only. NOTE: this script used to blame its own 0xc000027b
        # failures on the publish SHAPE (framework-dependent vs self-contained). That was wrong:
        # every published layout died because the build's locale-pruning target deleted WinUI's
        # `en-US\Microsoft.ui.xaml.dll.mui`; the target is gone and the check below keeps it gone.
        dotnet publish (Join-Path $repo "MostaqlK.csproj") -f $target -c $Configuration -r win-x64 `
            "-p:TargetFrameworks=$target" "-p:Channel=$ch" `
            "-p:PublishSingleFile=false" "-p:SelfContained=true" "-p:SatelliteResourceLanguages=ar" `
            -o $out --nologo -v q
        if ($LASTEXITCODE -ne 0) { throw "publish $ch failed" }

        # WinUI's Microsoft.ui.xaml.dll resolves its own strings from
        # <layout>\<lang>\Microsoft.ui.xaml.dll.mui and hard-fails ~4s into the first layout pass
        # (WinUI.UnhandledException 0x80004002 -> XAML fail-fast 0xc000027b) if the en-US fallback is
        # absent. A published layout without it is not a layout any user can run, so the smoke test
        # refuses to measure it.
        $mui = Join-Path $out "en-US\Microsoft.ui.xaml.dll.mui"
        if (-not (Test-Path $mui)) {
            throw "publish layout for $ch is missing the WinUI MUI fallback: $mui"
        }
    }

    $startedAt = $script:startedAt = (Get-Date).ToUniversalTime()

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
        $launches += [pscustomobject]@{ Channel = $ch; Process = $p; Exe = $exe }
        Write-Host "$ch launched pid=$($p.Id)" -ForegroundColor Gray
        Start-Sleep -Seconds 4
    }
    Write-Host "soaking both instances for $SoakSeconds seconds..." -ForegroundColor Gray
    Start-Sleep -Seconds $SoakSeconds

    $failures = @()
    foreach ($l in $launches) {
        $issue = Get-LivenessIssue $l.Process $l.Exe
        if ($issue) { $failures += "$($l.Channel): $issue" }
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
    $launched = @($started | ForEach-Object { $_.Id })
    $dumpType = $null
    try {
        $dumpType = (Get-ItemProperty "HKCU:\SOFTWARE\Microsoft\Windows\Windows Error Reporting\LocalDumps\MostaqlK.exe" -ErrorAction Stop).DumpType
    } catch { }
    foreach ($w in $werDump) {
        # The dump file is named MostaqlK.exe.<pid>.dmp. When that pid is NOT one of the launched
        # instances, the crash belongs to some other process of the same layout (a leftover
        # developer instance, a relaunch path) and says nothing about channel coexistence — so the
        # failure line has to say which case it is, or the next one costs an hour to interpret.
        $dumpedPid = [regex]::Match($w.Name, '\.(\d+)\.dmp$').Groups[1].Value
        $mine = $launched -contains [int]$dumpedPid
        $failures += "WER dump: $($w.FullName) (pid $dumpedPid, $(if ($mine) { 'LAUNCHED BY THIS RUN' } else { 'not a pid this run launched' }))"
        if (-not $dumpType -or [int]$dumpType -lt 2) {
            Write-Host "  hint: this is a mini dump - the stowed WinRT exception inside it is not" -ForegroundColor DarkGray
            Write-Host "        readable. tools/enable-full-dumps.ps1 turns on full dumps (opt-in)," -ForegroundColor DarkGray
            Write-Host "        and the [birth]/pid lines in the logs say who that process was." -ForegroundColor DarkGray
        }
    }

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

    # Second liveness pass: the soak plus every check above took real time, and an instance that
    # survived the soak only to die during the assertions must not be reported as coexisting.
    foreach ($l in $launches) {
        $lateIssue = Get-LivenessIssue $l.Process $l.Exe
        if ($lateIssue) { $failures += "$($l.Channel) died during the checks: $lateIssue" }
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
