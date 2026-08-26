---
name: version-bumping
description: Standardized end-to-end workflow for bumping application versions, synchronizing cross-platform manifests, updating Keep a Changelog files, configuring PE/Win32 metadata, verifying builds, and generating Git tags.
---

# Version Bumping and Release Standardization

## Overview

Version bumping is more than changing a single version number. In modern cross-platform and desktop applications (such as .NET MAUI, Windows App SDK, and multi-target projects), bumping a version requires **synchronized updates across multiple manifest files, project files, native PE metadata, user-facing changelogs, and version control tags**.

A missed property can lead to:
- Windows Explorer displaying `0.0.0.0` or blank file tooltips.
- App store deployment rejections due to non-incremented build codes.
- Inconsistent version reporting between in-app "About" pages and operating system packages.
- Misleading changelog records that make regression tracking impossible.

Use this skill whenever incrementing a version, cutting a release, or reviewing version-related changes.

---

## When to Use

- When preparing a new release (patch, minor, or major).
- After completing a feature or bugfix milestone that requires a version increment.
- When requested to "bump the version" or "prepare release notes".
- When creating or updating `CHANGELOG.md`.
- When diagnosing version metadata issues (e.g. `FileVersion`, `ProductVersion`, `0.0.0.0` in Windows Explorer).

---

## 1. Semantic Versioning (SemVer 2.0.0) Rules

Given a version number `MAJOR.MINOR.PATCH`:

| Component | When to increment | Example |
|-----------|-------------------|---------|
| **MAJOR** (`X.0.0`) | Incompatible API/database changes, breaking UI/workflow overhauls, major architectural rewrites. Resets `MINOR` and `PATCH` to `0`. | `1.2.3` → `2.0.0` |
| **MINOR** (`x.Y.0`) | New features, backward-compatible functional enhancements, non-breaking schema additions. Resets `PATCH` to `0`. | `1.2.3` → `1.3.0` |
| **PATCH** (`x.y.Z`) | Backward-compatible bug fixes, performance improvements, hotfixes, security patches. | `1.2.3` → `1.2.4` |

### Pre-release and Build Codes
- **Pre-release:** `1.0.3-preview.1`, `1.0.3-rc.1` (appended with a hyphen).
- **Build Code / Sequence Number (`ApplicationVersion`):** An integer that **strictly increases monotonically** with every build or store release (`1`, `2`, `3`, `4`, ...). Never decrement or repeat.

---

## 2. Multi-File Synchronization Checklist

Every version bump **MUST** synchronize all of the following locations:

### A. Project File (`MostaqlK.csproj`)
Update both user-facing semantic versions and native assembly/PE metadata:

```xml
<!-- User-facing and build codes -->
<ApplicationDisplayVersion>1.0.3</ApplicationDisplayVersion>
<ApplicationVersion>4</ApplicationVersion>

<!-- Assembly & Native Win32 PE Resource Metadata -->
<Version>1.0.3</Version>
<FileVersion>1.0.3.0</FileVersion>
<AssemblyVersion>1.0.3.0</AssemblyVersion>
<Description>MostaqlK Desktop</Description>
<Product>MostaqlK</Product>
<Company>MostaqlK</Company>
```

> **Crucial for Windows Single-File / Unpackaged Apps:**
> Windows Explorer reads hover tooltips and Properties dialogs from the Win32 `VS_VERSIONINFO` resource. If `<FileVersion>` and `<AssemblyVersion>` (4-part format: `Major.Minor.Patch.Revision`) are omitted, Windows will display `0.0.0.0`.

### B. Platform Manifests
- **Windows (`Platforms/Windows/Package.appxmanifest`):**
  Ensure the 4-part `<Identity>` version matches `<FileVersion>`:
  ```xml
  <Identity Name="com.mostaqlk.app" Publisher="CN=MostaqlK" Version="1.0.3.0" />
  ```
- **Android (`Platforms/Android/AndroidManifest.xml`):**
  If configured manually, ensure `android:versionCode` matches `<ApplicationVersion>` and `android:versionName` matches `<ApplicationDisplayVersion>`.
- **iOS/macOS (`Platforms/iOS/Info.plist`, `Platforms/MacCatalyst/Info.plist`):**
  Ensure `CFBundleShortVersionString` matches `<ApplicationDisplayVersion>` and `CFBundleVersion` matches `<ApplicationVersion>`.

### C. In-App About / Settings UI
- Never hardcode static version strings in XAML or code-behind.
- Always read the runtime version dynamically via `Microsoft.Maui.ApplicationModel.AppInfo`:
  ```csharp
  string displayVersion = AppInfo.Current.VersionString; // e.g. "1.0.3"
  string buildNumber = AppInfo.Current.BuildString;      // e.g. "4"
  ```

---

## 3. Changelog Maintenance (`CHANGELOG.md`)

Changelogs must follow the [Keep a Changelog 1.1.0](https://keepachangelog.com/en/1.1.0/) specification.

### Header Format
```markdown
# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.0.3] - 2026-08-26
```

### Standard Subsections (in exact order)
Use only the subsections relevant to the release:
1. `### Added` — for new features.
2. `### Changed` — for changes in existing functionality.
3. `### Deprecated` — for soon-to-be removed features.
4. `### Removed` — for now removed features.
5. `### Fixed` — for any bug fixes.
6. `### Security` — in case of vulnerabilities.

### Writing Good Changelog Entries
- Write for human users and developers, not raw commit dumps.
- Focus on the **outcome and impact** (what was fixed/added and why it matters).
- Group related changes into clean bullet points.
- Always include the release date in `YYYY-MM-DD` format (or `[Unreleased]` for work in progress).

---

## 4. Step-by-Step Version Bump Workflow

Follow this sequence systematically:

```
1. Determine Next Version (SemVer analysis: Major, Minor, or Patch)
                    │
                    ▼
2. Synchronize Project & Manifest Files (.csproj, Package.appxmanifest)
                    │
                    ▼
3. Update CHANGELOG.md (Add new version section with date & categorized notes)
                    │
                    ▼
4. Build & Publish Verification (dotnet build / publish)
                    │
                    ▼
5. Inspect Binary PE Metadata (Verify FileVersion, ProductVersion via PowerShell)
                    │
                    ▼
6. Commit & Tag (Git commit with conventional message + annotated tag)
```

### Step 1: Determine Next Version
Inspect the recent changes and git log to determine if the release is `PATCH`, `MINOR`, or `MAJOR`:
- Bug fixes only → Bump `PATCH` (e.g. `1.0.2` → `1.0.3`).
- New backward-compatible feature → Bump `MINOR` (e.g. `1.0.2` → `1.1.0`).
- Breaking changes → Bump `MAJOR` (e.g. `1.0.2` → `2.0.0`).

### Step 2: Edit Project Files & Manifests
Update:
1. `MostaqlK.csproj` (`ApplicationDisplayVersion`, `ApplicationVersion`, `Version`, `FileVersion`, `AssemblyVersion`).
2. `Platforms/Windows/Package.appxmanifest` (`Version` attribute).

### Step 3: Update `CHANGELOG.md`
1. Move items from `[Unreleased]` to `[X.Y.Z] - YYYY-MM-DD`.
2. Categorize all bullet points under `Added`, `Changed`, `Fixed`, etc.

### Step 4: Build & Publish Verification
Compile the project to confirm there are no syntax or configuration errors:
```powershell
cmd /c "dotnet build MostaqlK.csproj -f net10.0-windows10.0.19041.0 -c Release"
```

### Step 5: Verify Binary PE Metadata
Inspect the compiled DLL and single-file EXE to confirm Windows Explorer and OS readers see the updated version:
```powershell
cmd /c "powershell -NoProfile -Command ""(Get-Item 'bin\Release\net10.0-windows10.0.19041.0\win-x64\publish\MostaqlK.exe').VersionInfo | Format-List FileVersion, ProductVersion, FileDescription, CompanyName"""
```
**Verification Criteria:**
- `FileVersion` matches `X.Y.Z.0` (not `0.0.0.0`).
- `ProductVersion` contains `X.Y.Z`.
- `FileDescription` and `CompanyName` are populated properly.

### Step 6: Git Commit & Tag
When committing (or recommending git commands to the user):
```bash
git add MostaqlK.csproj Platforms/Windows/Package.appxmanifest CHANGELOG.md
git commit -m "chore(release): bump version to 1.0.3" --trailer "Co-authored-by: Junie <junie@jetbrains.com>"
git tag -a v1.0.3 -m "Release v1.0.3"
```

---

## 5. Common Pitfalls & Anti-Patterns

| Anti-Pattern | Why it fails | Correct Approach |
|--------------|--------------|------------------|
| Only changing `ApplicationDisplayVersion` | Windows binary host stub defaults to `0.0.0.0` in file properties. | Also set `<Version>`, `<FileVersion>`, and `<AssemblyVersion>` in `.csproj`. |
| Forgetting `<ApplicationVersion>` | Store submissions and local updater packages fail to detect newer revisions. | Always increment `<ApplicationVersion>` integer code (`1, 2, 3...`). |
| Inconsistent `Package.appxmanifest` version | MSIX / AppX packaging fails validation or mismatches package identity. | Keep `Package.appxmanifest` Version aligned with `<FileVersion>`. |
| Dumping raw git commits into `CHANGELOG.md` | Confusing to users and difficult to scan. | Synthesize entries into clear, categorized bullet points (`Added`, `Fixed`). |
| Hardcoded version strings in UI views | UI displays outdated version after bump. | Always bind or read from `AppInfo.Current.VersionString`. |
| Skipping PE metadata inspection | Releases ship with broken or missing metadata without author knowing. | Run PowerShell `VersionInfo` validation on build artifacts before shipping. |
