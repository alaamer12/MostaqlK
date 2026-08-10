using NUnit.Framework;
using MostaqlK.UITests.Utils;
using OpenQA.Selenium;
using System;
using System.IO;

[TestFixture]
public class UiDebuggerTests
{
    [Test]
    public void WaitAndFind_NonExistentAutomationId_DumpsPageSourceAndThrowsInformativeException()
    {
        Assert.That(AppiumSetup.Driver, Is.Not.Null, "Appium WindowsDriver should be initialized.");

        const string missingId = "This_AutomationId_Does_Not_Exist_12345";
        var timeout = TimeSpan.FromSeconds(2);

        var workDirectoryFilesBefore = Directory.GetFiles(TestContext.CurrentContext.WorkDirectory, "dump_*.xml");

        var ex = Assert.Throws<NoSuchElementException>(() =>
            UiDebugger.WaitAndFind(AppiumSetup.Driver!, missingId, timeout));

        Assert.That(ex!.Message, Does.Contain(missingId),
            "Exception message should mention the missing AutomationId.");
        Assert.That(ex.Message, Does.Contain("waiting").IgnoreCase,
            "Exception message should mention elapsed time.");

        var workDirectoryFilesAfter = Directory.GetFiles(TestContext.CurrentContext.WorkDirectory, "dump_*.xml");
        Assert.That(workDirectoryFilesAfter.Length, Is.GreaterThan(workDirectoryFilesBefore.Length),
            "A page source dump file should have been created on failure.");
    }

    [Test]
    public void WaitAndFind_KnownGoodWindow_ReturnsWithoutThrowing()
    {
        Assert.That(AppiumSetup.Driver, Is.Not.Null, "Appium WindowsDriver should be initialized.");

        // There are no AutomationIds wired up in the app yet (tracked in a separate,
        // in-progress step of the plan), so use the same "known-good" signal
        // AppLaunchTests.cs already relies on: a valid current window handle.
        var windowHandle = AppiumSetup.Driver!.CurrentWindowHandle;
        Assert.That(windowHandle, Is.Not.Null.And.Not.Empty, "Application window handle should be present.");
    }
}
