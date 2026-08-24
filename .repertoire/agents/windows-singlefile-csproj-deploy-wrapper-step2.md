# Step 2 report: single-file packaging props + deploy wrapper forwarding

## What I changed

### 1) `MostaqlK.csproj`
- Added Windows App SDK unpackaged single-file required properties **only when** `PublishSingleFile==true`:
  - `IncludeAllContentForSelfExtract=true`
  - `EnableMsixTooling=true`
  - `WindowsAppSDKSelfContained=true`

These are required by Windows App SDK's `WindowsAppSDKSingleFileVerifyConfiguration` validation target for unpackaged WinUI 3 apps.

### 2) `scripts/deploy.ps1`
- Added an optional `-Arch` parameter (`x64` default).
- When `-Platform Windows` is used, `deploy.ps1` now forwards `-Arch` to `scripts/release-windows.ps1`.

Other platforms preserve existing behavior (they ignore `-Arch`).

## Verification I performed (fast)

Ran the Windows App SDK single-file verify MSBuild target (no full publish) to confirm the project now satisfies the required MSBuild properties:

```cmd
dotnet msbuild MostaqlK.csproj /t:WindowsAppSDKSingleFileVerifyConfiguration /p:TargetFramework=net10.0-windows10.0.19041.0 /p:Configuration=Release /p:PublishSingleFile=true /p:SelfContained=true /p:RuntimeIdentifier=win-x64 /p:IncludeNativeLibrariesForSelfExtract=true /p:EnableCompressionInSingleFile=true
```

Result: **exit code 0**.

## Files touched
- `MostaqlK.csproj`
- `scripts/deploy.ps1`
