# ui-conventions-scout

Read-only conventions audit run ahead of the pipeline dashboard panel work. The subagent had no
file-creation tool, so this report is written by the master agent on its behalf.

## Goal

Establish exactly how a new collapsible side panel + drag-to-resize handle must be built so it
matches this codebase, before any code was written.

## Findings used by the implementation

- **Mechanism**: same shape on every platform → **Platform Component**, so
  `UI/PlatformComponents/PipelineDashboardPanel/` and `UI/PlatformComponents/SplitterHandle/`
  (`<Unit>.cs` + optional `<Unit>.Windows.cs`), namespace mirroring the folder.
- **Composite views** use XAML + partial code-behind with `x:Name="Root"` and
  `{Binding …, Source={x:Reference Root}}`; thin control subclasses stay pure C#.
- **BindableProperty**: static `propertyChanged` casts `bindable` and delegates to a private
  instance `Apply*()`; outward interaction is exposed as plain events.
- **AutomationId**: `<Page>_<Element>`, set on the control owning the gesture. A
  `Border`/`Label` + `TapGestureRecognizer` never surfaces an AutomationId to the Windows UIA tree
  (dotnet/maui#4715), so clickable rows need a transparent full-size `Button` overlay.
- **Theming**: real unit XAML uses inline `AppThemeBinding` Tailwind hex pairs (surface
  `White`/`#0F172A`, card `#F1F5F9`/`#1E293B`, border `#E2E8F0`/`#1E293B`, muted text
  `#64748B`/`#94A3B8`, accent `#2563EB`/`#60A5FA`), not the four keyed `*Base` styles.
- **Typography**: per-element `Tajawal` / `TajawalMedium` / `TajawalBold`; never an implicit
  `Label` style (it crashes startup on this unpackaged WinUI build).
- **RTL**: `FlowDirection` is set once at page level; use logical `Start`/`End`. A pan delta is
  physical, so the resize direction must be a call-site decision — implemented as
  `SplitterHandle.DragSign`.
- **Layout**: follow `AppSidebar`'s reason for a `Grid` over a `VerticalStackLayout` (a stack
  ignores a `VerticalOptions="Fill"` spacer), hence header/`ScrollView` rows in the panel.
- **Reduced motion**: `MotionPreferences` must gate the collapse animation, and the established
  fallback is "instant state, no travel" — never "no feedback".
- **Nothing pre-existing** for splitters, drag-resize or cursor manipulation; `ProtectedCursor` had
  to be reached by reflection. Persisted UI state is plain `Preferences` with a
  `private const string Key…` and a snake_case key.
- **No mockup** exists for a side dashboard column (`.repertoire/design/mvp/` has five files, none
  with a third column), so its visuals were derived from the existing palette.

## Verification

Read-only; no files were modified by the subagent.
