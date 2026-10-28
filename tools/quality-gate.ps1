# Quality gate: the one command that runs what a reviewer would otherwise check by hand.
#
# WHY THIS EXISTS. The repo has real gates - ParserTests (457 headless checks incl. source lints),
# CrashPathTests, .editorconfig code style, build-clean requirements on two TFMs, and the coexistence
# smoke - but they were run individually, from memory, and CI ran none of them. A gate nobody can
# name is a gate that gets skipped. Everything here is read-only: nothing reformats your files.
#
# Usage (from the repo root):
#   powershell -File tools\quality-gate.ps1                    # tests + repo-wide format verify
#   powershell -File tools\quality-gate.ps1 -FormatChanged     # format check only the working diff
#   powershell -File tools\quality-gate.ps1 -Build             # + Windows canary/stable rebuild
#   powershell -File tools\quality-gate.ps1 -Build -Android    # + android (needs JavaSdkDirectory)
#   powershell -File tools\quality-gate.ps1 -Smoke             # + coexistence smoke (publishes: slow)
#   powershell -File tools\quality-gate.ps1 -Quick             # tests only (what the pre-commit hook runs)
#
# Exit code is 0 only when every selected step passed. ASCII on purpose: Windows PowerShell 5.1
# reads a BOM-less UTF-8 .ps1 as ANSI, which corrupts em-dashes into a string delimiter and breaks
# the parse mid-file. That cost an hour once (see the coexistence smoke's history).

param (
    [switch]$Build,
    [switch]$Android,
    [switch]$Smoke,
    [switch]$Quick,
    [switch]$SkipFormat,
    [switch]$FormatChanged,
    [string]$JavaSdkDirectory = "D:\Program Files\Java\jdk-17.0.12",
    [int]$SoakSeconds = 30
)

$ErrorActionPreference = "Continue"
$repo = Split-Path $PSScriptRoot -Parent
Set-Location $repo

$target = "net10.0-windows10.0.19041.0"
# Script scope: a plain `$results += ...` inside Invoke-Step creates a function-local copy, the
# summary then reports "0 steps" and a failing suite still exits 0. That is the worst possible way
# for a gate to be wrong - green on the surface, checking nothing.
$results = New-Object System.Collections.ArrayList

function Invoke-Step {
    param([string]$Name, [scriptblock]$Body)

    Write-Host ""
    Write-Host "=== $Name ===" -ForegroundColor Cyan
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $ok = $true
    try {
        & $Body | ForEach-Object { Write-Host $_ }
        if ($LASTEXITCODE -ne 0) { $ok = $false }
    } catch {
        $ok = $false
        Write-Host "  step threw: $_" -ForegroundColor Red
    }
    $sw.Stop()
    [void]$script:results.Add([pscustomobject]@{ Step = $Name; Passed = $ok; Seconds = [int]$sw.Elapsed.TotalSeconds })
    if (-not $ok) { Write-Host "  FAILED ($([int]$sw.Elapsed.TotalSeconds)s)" -ForegroundColor Red }
    else { Write-Host "  passed ($([int]$sw.Elapsed.TotalSeconds)s)" -ForegroundColor Green }
}

# --- 1. Headless suites: fast, no MAUI workload, and they carry the source lints --------------
Invoke-Step "ParserTests" {
    dotnet run --project tools/ParserTests -c Debug --no-launch-profile 2>&1 |
        Select-String -Pattern "TOTAL:|FAIL |Failed checks" -SimpleMatch:$false
}

Invoke-Step "CrashPathTests" {
    dotnet run --project tools/CrashPathTests -c Debug --no-launch-profile 2>&1 |
        Select-String -Pattern "PASSED|FAILED|FAIL "
}

if ($Quick -or $SkipFormat) {
    Write-Host ""
    Write-Host "skipped format check (Quick/SkipFormat)" -ForegroundColor DarkGray
} else {
    # --- 2. Code style: .editorconfig rules, verified not applied -----------------------------
    # `--verify-no-changes` is the whole point: dotnet format WITHOUT it auto-applies fixes, and on a
    # single-project multi-TFM MAUI app one TFM cannot see the members another platform's partial
    # uses. That is why IDE0051/IDE0052 are pinned to `suggestion` in .editorconfig; keep this
    # command verify-only and the hazard stays theoretical.
    if ($FormatChanged) {
        $changed = @(git diff --name-only --diff-filter=d HEAD 2>$null | Where-Object { $_ -like "*.cs" })
        if ($changed.Count -eq 0) {
            Write-Host ""
            Write-Host "=== format (changed files) ===`n  no .cs changes; nothing to check" -ForegroundColor Cyan
            [void]$script:results.Add([pscustomobject]@{ Step = "format (changed files)"; Passed = $true; Seconds = 0 })
        } else {
            Invoke-Step "format ($($changed.Count) changed .cs)" {
                dotnet format MostaqlK.csproj --verify-no-changes --severity warn --include ($changed -join " ") 2>&1 |
                    Select-String -Pattern "error |Formatting would be necessary"
            }
        }
    } else {
        Invoke-Step "format (repo wide, severity warn)" {
            dotnet format MostaqlK.csproj --verify-no-changes --severity warn 2>&1 |
                Select-String -Pattern "error |Formatting would be necessary"
        }
    }
}

if ($Build) {
    # --- 3. Builds: EnforceCodeStyleInBuild is on in the csproj, so a build IS a style gate ----
    Invoke-Step "build windows canary" {
        dotnet build MostaqlK.csproj -f $target -c Debug -p:Channel=canary --nologo -v q 2>&1 |
            Select-String -Pattern "error|warning |Warning\(s\)|Error\(s\)"
    }
    Invoke-Step "build windows stable" {
        dotnet build MostaqlK.csproj -f $target -c Debug -p:Channel=stable --nologo -v q 2>&1 |
            Select-String -Pattern "error|warning |Warning\(s\)|Error\(s\)"
    }
}

if ($Android) {
    Invoke-Step "build android" {
        dotnet build MostaqlK.csproj -f net10.0-android -c Debug -p:JavaSdkDirectory="$JavaSdkDirectory" --nologo -v q 2>&1 |
            Select-String -Pattern "error|warning |Warning\(s\)|Error\(s\)"
    }
}

if ($Smoke) {
    # Publishes both channels: minutes, not seconds. Use -SkipPublish afterwards for re-runs.
    Invoke-Step "coexistence smoke (${SoakSeconds}s soak)" {
        powershell -NoProfile -ExecutionPolicy Bypass -File tools/coexistence-smoke.ps1 -SoakSeconds $SoakSeconds 2>&1 |
            Select-String -Pattern "PASSED|FAILED|launched|soaking|WER dump|WARNING"
    }
}

# --- Verdict ---------------------------------------------------------------------------------
Write-Host ""
Write-Host ("=" * 62)
$failed = @($script:results | Where-Object { -not $_.Passed })
foreach ($r in $script:results) {
    $mark = if ($r.Passed) { "PASS" } else { "FAIL" }
    Write-Host ("  {0,-32} {1,4}  {2}s" -f $r.Step, $mark, $r.Seconds)
}
Write-Host ("=" * 62)
if ($failed.Count -gt 0) {
    Write-Host "QUALITY GATE FAILED: $($failed.Count) of $($script:results.Count) steps" -ForegroundColor Red
    exit 1
}
Write-Host "QUALITY GATE PASSED ($($script:results.Count) steps)" -ForegroundColor Green
exit 0
