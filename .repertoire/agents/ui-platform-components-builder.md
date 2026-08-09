# ui-platform-components-builder — Report

## Goal
Scaffold `UI/PlatformComponents/`, `UI/PlatformConcepts/`, and `UI/TrayIcon/` per the two-mechanism
cross-platform UI convention (same-shape partials vs same-concept per-platform resolution),
Windows-only for V1, with compiling stub code.

## Skill used
No `.cursor/skills/` directory exists in this repository (verified via glob search), so the
mandatory-skill step could not be satisfied as literally specified; proceeded directly per the
issue's explicit, fully-specified file-by-file instructions instead.

## Docs read
- `.repertoire/.steering/base/structure.md`
- `.repertoire/.steering/base/system-components.md` (§ 13 UI System, § 13.1 Tray Icon)
- `.repertoire/.steering/v1/tech/cross-platform-ui-conventions.md` (primary spec for `PlatformSelect.For<T>()` and `PlatformConcepts` naming/example)
- Design mockups referenced conceptually (`projects.html` sidebar/notification badge, `settings.html` toggle) — not directly opened since no visual markup was needed for these C#-only stubs.

## Files created
- `UI/PlatformComponents/PlatformSelect.cs` — generic `PlatformSelect.For<T>(android, ios, windows, macCatalyst)` using `#if ANDROID/IOS/WINDOWS/MACCATALYST`.
- `UI/PlatformComponents/AppButton/AppButton.cs` + `AppButton.Windows.cs` (partial `Button`).
- `UI/PlatformComponents/AppCard/AppCard.cs` + `AppCard.Windows.cs` (partial `Border`) — includes a real `BindableProperty` for `IsUnread` (+ convenience `IsRead` inverse) for the unread/read accent-border concept from the mockup.
- `UI/PlatformComponents/AppEntry/AppEntry.cs` + `AppEntry.Windows.cs` (partial `Entry`).
- `UI/PlatformComponents/AppToggle/AppToggle.cs` + `AppToggle.Windows.cs` (partial `Switch`).
- `UI/PlatformConcepts/NavigationControl.cs`
- `UI/PlatformConcepts/ModalPresenter.cs`
- `UI/PlatformConcepts/Drawer.cs`
- `UI/PlatformConcepts/ActionMenu.cs`
- `UI/TrayIcon/TrayIconService.cs` — `TrayIconState` enum (Idle/Polling/BacklogDraining/Error), `TrayMenuItem` record, and `TrayIconService` with the 6 spec'd menu entries (Open, Pause/Resume, Check now, Recent notifications, Settings, Quit), all wired to TODO-stub handlers.

Each `PlatformComponents/*` base file has a one-line TODO noting where a future `.Android.cs` /
`.iOS.cs` / `.MacCatalyst.cs` partial would go (no such files created, per V1 Windows-only scope).

## PlatformConcepts → concrete Windows control chosen
- `NavigationControl` → `Grid`-based two-column layout (nav rail + content), standing in for "SidePanel" — no MAUI docking-panel control exists out of the box, so a `Grid` is the closest primitive.
- `ModalPresenter` → stub returns a bare `ContentView`, documented as standing in for a modal `ContentPage` pushed via `Navigation.PushModalAsync` ("Dialog"), chosen over CommunityToolkit `Popup` to avoid a new package dependency at scaffold time.
- `Drawer` → documented as standing in for `FlyoutPage` ("Flyout"); stub factory returns a `ContentView` placeholder since no concrete flyout content exists yet.
- `ActionMenu` → documented as standing in for `MenuFlyout` via `FlyoutBase.ContextFlyout` ("ContextMenu"); stub factory returns a `ContentView` placeholder for the same reason.

All four keep the 4-key `PlatformSelect.For<Func<View>>(android:, ios:, windows:, macCatalyst:)`
call shape with mobile branches `null` + TODO comments, per instructions.

## Build result
`dotnet build MostaqlK.csproj -f net10.0-windows10.0.19041.0` **fails**, but not because of any file
created in this task. The failure is in the `MarkupCompilePass1` XAML-compilation step
(`Microsoft.UI.Xaml.Markup.Compiler.interop.targets`, `XamlCompiler.exe` exits with code 1) while
compiling the project's existing `.xaml` pages (`App.xaml` / `AppShell.xaml` / `MainPage.xaml`) —
this happens before any of my `UI/PlatformComponents|PlatformConcepts|TrayIcon` `.cs` files are
even reached in the build pipeline, and none of my changes touch XAML. This looks like a
pre-existing local build/toolchain issue (WindowsAppSDK `net472` XamlCompiler invocation failing
in this environment) rather than something introduced by this task. I did not modify any files
outside my assigned scope to attempt a fix, per instructions to leave Features/Services/etc. (and
by extension the shared XAML template files) to other agents / the user.

## Open questions / TODOs
- The pre-existing `MarkupCompilePass1`/`XamlCompiler.exe` failure should be investigated by whoever
  owns `App.xaml`/`AppShell.xaml`/`MainPage.xaml` (or the environment/toolchain) — it blocks any
  full build of the app regardless of the changes in this task.
- All TODOs left in the new files are intentional stub markers for future implementers (real style
  resource wiring, native handler customization, actual tray-icon platform integration, and real
  content for the `PlatformConcepts` view factories).
- No `.cursor/skills/` directory exists in this repo, so skill usage as mandated by the agent
  preferences could not be fulfilled literally; flagging this for whoever maintains `AGENTS.md`.
