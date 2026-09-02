# Deploy / Publish production build
param (
    [Parameter(Mandatory=$false)]
    [ValidateSet("Windows", "macOS", "Android", "iOS", "Mobile")]
    [string]$Platform = "Windows",

    [Parameter(Mandatory=$false)]
    [ValidateSet("x64", "arm64", "x86")]
    [string]$Arch = "x64",

    [Parameter(Mandatory=$false)]
    [ValidateSet("Portable", "Directory")]
    [string]$Type = "Directory",

    [Parameter(Mandatory=$false)]
    [switch]$ResetDatabase
)

$extraArgs = @()
if ($ResetDatabase) {
    $extraArgs += "-ResetDatabase"
}

switch ($Platform) {
    "Windows" {
        & "$PSScriptRoot\release-windows.ps1" -Type $Type -Arch $Arch @extraArgs
    }
    "macOS" {
        & "$PSScriptRoot\release-macos.ps1" @extraArgs
    }
    "Android" {
        & "$PSScriptRoot\release-mobile.ps1" -Platform "Android" @extraArgs
    }
    "iOS" {
        & "$PSScriptRoot\release-mobile.ps1" -Platform "iOS" @extraArgs
    }
    "Mobile" {
        & "$PSScriptRoot\release-mobile.ps1" -Platform "Both" @extraArgs
    }
}
