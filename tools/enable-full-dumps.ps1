# Opt-in full crash dumps for this app, via per-user WER LocalDumps.
#
# WHY THIS EXISTS. A WinUI/COM fail-fast (`RoFailFastWithErrorContext` ->
# `RaiseFailFastException`, status 0xC000027B) terminates the process through a path that can
# bypass the app's own `SetUnhandledExceptionFilter`, so the only artifact left is whatever WER
# wrote into %LocalAppData%\CrashDumps. WER's default is a mini dump: enough for the faulting
# thread's module+offset, NOT enough to read the stowed WinRT exception hanging off
# ExceptionInformation[1] - which is the part that names the actual failure. The long-idle
# postmortem hit exactly this wall (docs/postmoterm/long-idle-background-crash-full-investigation.md
# §4-6: "cannot read stowed ptr ... not in any memory range captured by this minidump").
#
# A full dump captures the heap, so `scratch/dump.py` + a symbol walk can recover the inner
# HRESULT instead of guessing from offsets. Full dumps are large (hundreds of MB each), so this is
# a hunting-day switch, not a default, and it is per-user (HKCU) - no admin rights, no machine
# change, and -Restore removes it completely.
#
# Usage:
#   powershell -File tools\enable-full-dumps.ps1            # turn on (keeps 3 dumps, 2 GB cap)
#   powershell -File tools\enable-full-dumps.ps1 -Restore   # delete the setting again

param (
    [switch]$Restore,
    [string]$Executable = "MostaqlK.exe",
    [int]$Keep = 3
)

$ErrorActionPreference = "Stop"
$keyPath = "HKCU:\SOFTWARE\Microsoft\Windows\Windows Error Reporting\LocalDumps"
$target = Join-Path $keyPath $Executable

if ($Restore) {
    if (Test-Path $target) {
        Remove-Item -Path $target -Force
        Write-Host "Removed full-dump configuration for $Executable." -ForegroundColor Green
    } else {
        Write-Host "Nothing to restore for $Executable." -ForegroundColor Gray
    }
    exit 0
}

if (-not (Test-Path $keyPath)) {
    New-Item -Path $keyPath -Force | Out-Null
}

$dumpFolder = Join-Path $env:LOCALAPPDATA "CrashDumps"
New-Item -ItemType Directory -Force -Path $dumpFolder | Out-Null

New-Item -Path $target -Force | Out-Null
# 2 = DumpTypeFull (0 mini, 1 heap). Heap dumps resolve the faulting stack but not the stowed
# object's memory, which is the specific thing that was unrecoverable last time.
Set-ItemProperty -Path $target -Name DumpType -Type DWord -Value 2
Set-ItemProperty -Path $target -Name DumpFolder -Type String -Value $dumpFolder
Set-ItemProperty -Path $target -Name DumpCount -Type DWord -Value $Keep

Write-Host "Full WER dumps enabled for $Executable -> $dumpFolder (keeping $Keep)." -ForegroundColor Green
Write-Host "Remember: powershell -File tools\enable-full-dumps.ps1 -Restore when the hunt is over." -ForegroundColor Gray
