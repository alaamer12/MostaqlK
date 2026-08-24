---
sessionId: session-260824-162454-zuhq
---

# Requirements

### Overview & Goals
The objective is to fix `scripts/release-windows.ps1` (and `MostaqlK.csproj` publish cleanup) so that running with `-Type Portable` produces strictly a clean single standalone `.exe` file (`MostaqlK.exe`) in the output directory without leaving behind extraneous files (like `.pdb` files) or leftover empty folders (`ar`, `ar-SA`, `Microsoft.UI.Xaml`, `NpuDetect`, `Resources`).

### Scope
- **In Scope:**
  - Diagnosing and eliminating leftover artifacts and empty directory structures created during MSBuild publish in Portable single-file mode.
  - Adding cleanup logic and configuration in `scripts/release-windows.ps1` (and/or MSBuild properties) to strip `.pdb` debug symbols and delete empty subdirectories when publishing with `-Type Portable`.
  - Ensuring Directory mode (`-Type Directory`) remains untouched with full unpackaged assets.
  - Verifying the portable publish output folder contains solely `MostaqlK.exe`.
- **Out of Scope:**
  - Modifying non-Windows platforms (Android, iOS, macOS).
  - Changing application UI or runtime behavior.

### Acceptance Criteria
- Running `powershell scripts/release-windows.ps1 -Type Portable` produces an output directory containing only the standalone `MostaqlK.exe` executable.
- Running `powershell scripts/release-windows.ps1 -Type Directory` continues to produce the unpacked folder structure.
- `scripts/deploy.ps1 -Platform Windows -Type Portable` passes parameters and produces the clean single `.exe`.

# Technical Design

### Current Implementation
In `scripts/release-windows.ps1`, `dotnet publish` with `-p:PublishSingleFile=true` packages runtime assemblies and assets into `MostaqlK.exe`. However:
1. `dotnet publish` generates `MostaqlK.pdb` (debug symbol file) by default in Release.
2. Build targets for Windows App SDK and MAUI assets create intermediate folder structures (`ar`, `ar-SA`, `Microsoft.UI.Xaml\Assets`, `NpuDetect`, `Resources\Images`) in the publish folder prior to single-file bundling, leaving behind empty subdirectories in the final publish folder.

### Proposed Changes
1. **Update `scripts/release-windows.ps1` for Portable Mode**:
   - Add `-p:DebugType=None` and `-p:DebugSymbols=false` in `$publishArgs` for Portable mode to avoid emitting `.pdb` files.
   - Add post-publish cleanup in `scripts/release-windows.ps1` when `$Type -eq "Portable"`:
     - Remove any leftover empty directories recursively inside `$outputBase`.
     - Remove any leftover `.pdb` files or non-exe artifacts if present.
     - Ensure `$outputBase` strictly contains only `$projectName.exe`.
2. **Update `MostaqlK.csproj` (if necessary)**:
   - Ensure publish targets like `CleanupUnwantedLocales` handle single-file publishing cleanly.

### File Structure
- `scripts/release-windows.ps1` (modified)
- `MostaqlK.csproj` (verified)

### ✓ Step 1: Update scripts/release-windows.ps1 with symbol suppression and portable directory cleanup
Add `-p:DebugType=None` and `-p:DebugSymbols=false` for Portable mode, and add post-publish cleanup to remove leftover subdirectories and non-exe files in `$outputBase`.

### ✓ Step 2: Update MostaqlK.csproj publish targets if needed
Verify whether any csproj target changes are needed to prevent or handle intermediate publish directories during single-file publish.

### ✓ Step 3: Build verification and validation
Validate that `-Type Portable` generates solely `MostaqlK.exe` in the publish folder, `-Type Directory` generates the unpacked files, and `scripts/deploy.ps1` succeeds.