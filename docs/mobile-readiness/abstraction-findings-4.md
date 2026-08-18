# Abstraction Findings — Agent 4 (Inputs / Formatters / Cards)

Audit scope: `UI/PlatformComponents/*`, `UI/DesignSystem/*`, `Core/Formatting/*`, `ViewModels`.
Target specialization pattern per `UNITS.md`: `AppEntry` → `DebouncedEntry` → `SearchInputField`
(shared base + composed specializations).

## Finding 1: `DesignTokens.cs` is mislabeled "Scaffold" in UNITS.md — it's actually Implemented
- File: `UI/DesignSystem/DesignTokens.cs`:1-53
- Description: `UNITS.md` (Design System table) lists `DesignTokens` status as `Scaffold`, but the file contains real, populated brand colors (`AccentPrimary`, `AccentPositive`, `BackgroundLight/Dark`, `SurfaceLight/Dark`, `ReadBorderLight/Dark`), a real `Spacing` scale (`XS`..`XL`), and real `CornerRadius` tokens (`Small`/`Default`/`Large`). There is no stub/TODO anywhere in the file — this is a complete, usable static class. Confirms the prior audit's note.
- Suggested fix: Update `UNITS.md`'s Design System table row for `DesignTokens` from `Scaffold` to `Implemented`.

## Finding 2: `TruncatingLabel.cs` is genuinely a scaffold — `MaxChars` is a no-op
- File: `UI/DesignSystem/TruncatingLabel.cs`:1-26
- Description: The class only sets `LineBreakMode = LineBreakMode.TailTruncation` in its constructor. The `MaxChars` bindable property is declared but never consumed — there is no `Text`/`MaxChars` change handler that actually caps the string and appends "…". Two explicit `TODO` comments (lines 6, 23) confirm this is intentionally unfinished. `UNITS.md`'s `Scaffold` label for this unit is accurate, unlike `DesignTokens`.
- Suggested fix: Implement an `OnPropertyChanged`/bindable-property-changed handler that truncates `Text` to `MaxChars` and appends `…` when exceeded, matching the doc comment's stated intent.

## Finding 3: `ProjectFeedViewModel` hand-rolls the same debounce pattern as `DebouncedEntry` instead of reusing a shared unit
- File: `Features/Projects/ViewModels/ProjectFeedViewModel.cs`:50, 175-191 (compare `UI/PlatformComponents/DebouncedEntry/DebouncedEntry.cs`:6-84)
- Description: `DebouncedEntry` implements a "cancel-and-restart `CancellationTokenSource` on every new event" debounce (its documented job per `UNITS.md`: "restart-on-keystroke pattern"). `ProjectFeedViewModel.ScheduleAutoReload`/`DebouncedReloadAsync` independently reimplements the exact same shape (own `_autoReloadDebounce` field, `Interlocked.Exchange` + `previous?.Cancel()`/`Dispose()`, `Task.Delay(400, token)` inside a `try/catch (TaskCanceledException)`) to debounce pipeline events into a single `LoadAsync`. This is the same mechanism duplicated across the UI-control layer and the ViewModel layer with no shared abstraction.
- Suggested fix: Extract the restart-on-event debounce mechanics (the `CancellationTokenSource` swap-and-cancel pattern) into a small reusable non-UI helper (e.g. `Core/Debouncer` or similar), and have both `DebouncedEntry` and `ProjectFeedViewModel` compose it, following the same "shared base + specialization" idea as `AppEntry`→`DebouncedEntry`.

## Finding 4: `SettingsViewModel` repeats the same range-validation shape across four unrelated properties
- File: `Features/Settings/ViewModels/SettingsViewModel.cs`:374-421, 466-482
- Description: `OnPollIntervalSecondsChanged`, `OnRequestsPerMinuteChanged`, `OnMaxConcurrentDetailFetchesChanged`, and `OnGroupingThresholdChanged` each independently repeat the identical shape: guard on `_isLoading`, check a numeric bound, call `SetValidationError(message)` and `return` on failure, otherwise `ClearValidationError()` then persist+apply. There is no shared numeric-range validator helper — each partial method hand-rolls the same control flow with only the bound and Arabic message text varying.
- Suggested fix: Add a small private helper, e.g. `bool TryValidateRange(int value, int min, int max, string errorMessage)`, that centralizes the bound-check + `SetValidationError`/`ClearValidationError` calls, and have each `OnXChanged` partial call it before persisting.

## Finding 5: Skill-tag splitting/formatting duplicated ad hoc inside `ProjectCardViewModel` instead of a `Core/Formatting` unit
- File: `Features/Projects/ViewModels/ProjectCardViewModel.cs`:173-202
- Description: `SkillTags`, `SkillsDisplay`, and `SkillItems` all independently re-derive the same parsed skill list from `Project.SkillsText` (split on `,،|;`, trim, filter empty, take 6) via three separate computed properties, none of which live in `Core/Formatting` alongside `BudgetFormatter`/`ArabicRelativeTime`/`LastScanText` — the established location per `UNITS.md`'s "Display formatters" section for shared, view-model-reusable presentation logic. If any other card/detail view-model ever needs the same skill-chip parsing, it would have no shared place to pull it from and would likely re-implement the same split/trim/take-6 logic again.
- Suggested fix: Promote the skills-parsing logic to a static `SkillsFormatter` (or similar) class in `Core/Formatting/`, consumed by `SkillTags` (and any future consumer, e.g. `ProjectDetailsViewModel`), matching the existing formatter pattern.

## Summary
- Finding 1: `DesignTokens.cs` is mislabeled "Scaffold" in UNITS.md — it's actually Implemented
- Finding 2: `TruncatingLabel.cs` is genuinely a scaffold — `MaxChars` is a no-op
- Finding 3: `ProjectFeedViewModel` hand-rolls the same debounce pattern as `DebouncedEntry` instead of reusing a shared unit
- Finding 4: `SettingsViewModel` repeats the same range-validation shape across four unrelated properties
- Finding 5: Skill-tag splitting/formatting duplicated ad hoc inside `ProjectCardViewModel` instead of a `Core/Formatting` unit
