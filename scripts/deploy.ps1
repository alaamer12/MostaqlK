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
    [string]$Type = "Directory"
)

switch ($Platform) {
    "Windows" {
        & "$PSScriptRoot\release-windows.ps1" -Type $Type -Arch $Arch
    }
    "macOS" {
        & "$PSScriptRoot\release-macos.ps1"
    }
    "Android" {
        & "$PSScriptRoot\release-mobile.ps1" -Platform "Android"
    }
    "iOS" {
        & "$PSScriptRoot\release-mobile.ps1" -Platform "iOS"
    }
    "Mobile" {
        & "$PSScriptRoot\release-mobile.ps1" -Platform "Both"
    }
}
