using NUnit.Framework;

[TestFixture]
public class AppLaunchTests
{
    [Test]
    public void TestApplicationLaunchedSuccessfully()
    {
        Assert.That(AppiumSetup.Driver, Is.Not.Null, "Appium WindowsDriver should be initialized.");
        
        // Minimalist check: verify session has a valid window handle or app is running
        var windowHandle = AppiumSetup.Driver?.CurrentWindowHandle;
        Assert.That(windowHandle, Is.Not.Null.And.Not.Empty, "Application window handle should be present.");
    }
}
