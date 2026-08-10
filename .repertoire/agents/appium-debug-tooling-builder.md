# appium-debug-tooling-builder

## Goal
Step 3 of `.junie/plans/appium-ui-test-catalog-and-fixes.md`: extend the Appium/WinAppDriver harness in `MostaqlK.UITests/` with a debug-dump utility (`UiDebugger`), expose the shared driver session for reuse, and add a test proving the tool actually dumps the UI Automation tree and produces an informative exception on a missing element — without touching any file outside `MostaqlK.UITests/`.

## Actions taken
1. Read `.junie/plans/appium-ui-test-catalog-and-fixes.md` (Step 3 scope) and inspected the existing `MostaqlK.UITests/AppiumSetup.cs` and `AppLaunchTests.cs`.
2. Confirmed via `grep_search` that no `AutomationId` exists anywhere in the app's XAML yet (other in-progress work), so no real app AutomationId could be used as a "known-good" target.
3. Created `MostaqlK.UITests/Utils/UiDebugger.cs`:
   - `DumpPageSource(driver, label)` — writes `driver.PageSource` to `TestContext.WriteLine` and to a timestamped file `dump_{label}_{timestamp}.xml` under `TestContext.CurrentContext.WorkDirectory`; returns the file path.
   - `WaitAndFind(driver, automationId, timeout = null)` — polls `FindElementByAccessibilityId` every 500ms until a default/explicit timeout (10s default), and on failure calls `DumpPageSource` with a descriptive label, then throws `NoSuchElementException` including the AutomationId, elapsed time, and the dump file path.
   - `WaitAndClick(driver, automationId, timeout = null)` — calls `WaitAndFind` then `.Click()`, with the same dump-on-failure behavior wrapping the click itself.
4. Created `MostaqlK.UITests/UiDebuggerTests.cs` with two tests:
   - `WaitAndFind_KnownGoodWindow_ReturnsWithoutThrowing` — uses the same "known-good" signal `AppLaunchTests.cs` already relies on (a valid `CurrentWindowHandle`), since no real `AutomationId` exists in the app yet to target.
   - `WaitAndFind_NonExistentAutomationId_DumpsPageSourceAndThrowsInformativeException` — calls `WaitAndFind` with a deliberately-missing AutomationId (2s timeout), asserts the thrown `NoSuchElementException` message contains the missing id and mentions elapsed time/waiting, and asserts a new `dump_*.xml` file appears in the work directory.
5. Checked `AppiumSetup.cs`: `Driver` is already `public static WindowsDriver<WindowsElement>? Driver { get; private set; }` — already broader than the requested `protected/internal static`, so no visibility change was needed. Left the file untouched.
6. Built `MostaqlK.UITests` via `dotnet build` — succeeded, 0 warnings, 0 errors.

## Files touched/created
- `MostaqlK.UITests/Utils/UiDebugger.cs` (new)
- `MostaqlK.UITests/UiDebuggerTests.cs` (new)
- No other files were modified; `AppiumSetup.cs` and `AppLaunchTests.cs` were inspected only, not edited.

## Decisions
- Did not modify `AppiumSetup.cs` since `Driver` was already exposed as `public static` (satisfies "protected static or internal static" requirement more permissively).
- Since no `AutomationId` exists anywhere in the app yet (a separate, in-progress plan step), used the existing window-handle check pattern from `AppLaunchTests.cs` as the "known-good" case instead of inventing a placeholder AutomationId.
- Kept `UiDebugger` under its own `MostaqlK.UITests.Utils` namespace for reuse by future per-page test classes.

## Verification
- `dotnet build MostaqlK.UITests\MostaqlK.UITests.csproj -c Debug` → Build succeeded, 0 warnings, 0 errors.
- WinAppDriver was available on this machine (`C:\Program Files (x86)\Windows Application Driver\WinAppDriver.exe` exists) and the built app existed at `bin\Debug\net10.0-windows10.0.19041.0\win-x64\MostaqlK.exe`, so I was able to **actually run** the tests end-to-end (not just compile them):
  - `dotnet test --filter FullyQualifiedName~UiDebuggerTests` → **2/2 passed**. The failure-path test genuinely produced a page-source dump file (`dump_WaitAndFind_failed_This_AutomationId_Does_Not_Exist_12345_*.xml`) under the test output directory and the exception message contained the missing AutomationId and elapsed-time wording, confirming the dump-on-failure behavior works against the real running app.
  - `dotnet test --filter FullyQualifiedName~AppLaunchTests` → existing smoke test still **passes** (1/1), confirming this change did not break it.
- Environment note: Developer Mode/WinAppDriver **was** available in this environment, so this step's deliverable was verified with a real end-to-end run, not just a compile check.
