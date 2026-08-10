using NUnit.Framework;
using OpenQA.Selenium;
using OpenQA.Selenium.Appium.Windows;
using System;
using System.IO;
using System.Threading;

namespace MostaqlK.UITests.Utils;

/// <summary>
/// "Duck debugging" helpers for the Appium/WinAppDriver test harness: dumps the live UI
/// Automation tree on demand, and wraps element lookup/click with retry + on-failure dump so
/// every test failure is self-documenting instead of a bare NoSuchElementException.
/// </summary>
public static class UiDebugger
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Writes the driver's current page source to NUnit's TestContext output and to a
    /// timestamped file under TestContext.CurrentContext.WorkDirectory. Returns the file path.
    /// </summary>
    public static string DumpPageSource(WindowsDriver<WindowsElement> driver, string label)
    {
        string pageSource;
        try
        {
            pageSource = driver.PageSource;
        }
        catch (Exception ex)
        {
            pageSource = $"<failed to capture PageSource: {ex.Message}>";
        }

        TestContext.WriteLine($"[UiDebugger] Dumping page source for '{label}':");
        TestContext.WriteLine(pageSource);

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff");
        var fileName = $"dump_{label}_{timestamp}.xml";
        var filePath = Path.Combine(TestContext.CurrentContext.WorkDirectory, fileName);

        try
        {
            File.WriteAllText(filePath, pageSource);
            TestContext.WriteLine($"[UiDebugger] Page source written to: {filePath}");
        }
        catch (Exception ex)
        {
            TestContext.WriteLine($"[UiDebugger] Failed to write page source dump to '{filePath}': {ex.Message}");
        }

        return filePath;
    }

    /// <summary>
    /// Polls FindElementByAccessibilityId with retry until the timeout elapses. On failure,
    /// dumps the current page source (with a descriptive label) before rethrowing a clear
    /// exception that includes the automationId and elapsed time.
    /// </summary>
    public static WindowsElement WaitAndFind(WindowsDriver<WindowsElement> driver, string automationId, TimeSpan? timeout = null)
    {
        var effectiveTimeout = timeout ?? DefaultTimeout;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        Exception? lastException = null;

        while (stopwatch.Elapsed < effectiveTimeout)
        {
            try
            {
                var element = driver.FindElementByAccessibilityId(automationId);
                if (element != null)
                {
                    return element;
                }
            }
            catch (Exception ex)
            {
                lastException = ex;
            }

            Thread.Sleep(PollInterval);
        }

        stopwatch.Stop();

        var label = $"WaitAndFind_failed_{automationId}";
        var dumpPath = DumpPageSource(driver, label);

        var message = $"Could not find element with AutomationId '{automationId}' after waiting " +
                       $"{stopwatch.Elapsed.TotalSeconds:F1}s (timeout: {effectiveTimeout.TotalSeconds:F1}s). " +
                       $"A page source dump was written to: {dumpPath}";

        throw new NoSuchElementException(message, lastException);
    }

    /// <summary>
    /// Finds the element by AutomationId via WaitAndFind, then clicks it. On failure the same
    /// dump-on-failure behavior applies (via WaitAndFind, or on the click itself).
    /// </summary>
    public static WindowsElement WaitAndClick(WindowsDriver<WindowsElement> driver, string automationId, TimeSpan? timeout = null)
    {
        var element = WaitAndFind(driver, automationId, timeout);

        try
        {
            element.Click();
        }
        catch (Exception ex)
        {
            var label = $"WaitAndClick_failed_{automationId}";
            var dumpPath = DumpPageSource(driver, label);

            var message = $"Found element with AutomationId '{automationId}' but the click failed. " +
                           $"A page source dump was written to: {dumpPath}";

            throw new WebDriverException(message, ex);
        }

        return element;
    }
}
