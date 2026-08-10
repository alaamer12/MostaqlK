using NUnit.Framework;
using OpenQA.Selenium.Appium;
using OpenQA.Selenium.Appium.Windows;
using System;
using System.Diagnostics;
using System.IO;

[SetUpFixture]
public class AppiumSetup
{
    public static WindowsDriver<WindowsElement>? Driver { get; private set; }
    private static Process? _winAppDriverProcess;

    [OneTimeSetUp]
    public void RunBeforeAnyTests()
    {
        // 1. Automatically start WinAppDriver before test session
        StartWinAppDriver();

        // Path to built .NET 10 MAUI Windows app executable dynamically without hardcoded absolute path
        var baseDir = AppContext.BaseDirectory;
        var appPath = string.Empty;

        // Traverse upwards from test base directory to find solution root or bin folder dynamically
        var currentDir = new DirectoryInfo(baseDir);
        while (currentDir != null)
        {
            var candidate = Path.Combine(currentDir.FullName, "bin", "Debug", "net10.0-windows10.0.19041.0", "win-x64", "MostaqlK.exe");
            if (File.Exists(candidate))
            {
                appPath = candidate;
                break;
            }

            // Also check if we are already inside a workspace root containing MostaqlK.csproj
            var csprojCandidate = Path.Combine(currentDir.FullName, "MostaqlK.csproj");
            if (File.Exists(csprojCandidate))
            {
                var binCandidate = Path.Combine(currentDir.FullName, "bin", "Debug", "net10.0-windows10.0.19041.0", "win-x64", "MostaqlK.exe");
                if (File.Exists(binCandidate))
                {
                    appPath = binCandidate;
                    break;
                }
            }

            currentDir = currentDir.Parent;
        }

        // Fallback relative relative searches from base directory or current directory
        if (string.IsNullOrEmpty(appPath) || !File.Exists(appPath))
        {
            string[] relativePaths = {
                Path.Combine(baseDir, "..", "..", "..", "..", "bin", "Debug", "net10.0-windows10.0.19041.0", "win-x64", "MostaqlK.exe"),
                Path.Combine(baseDir, "..", "..", "..", "bin", "Debug", "net10.0-windows10.0.19041.0", "win-x64", "MostaqlK.exe"),
                Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "..", "bin", "Debug", "net10.0-windows10.0.19041.0", "win-x64", "MostaqlK.exe"),
                Path.Combine(Directory.GetCurrentDirectory(), "bin", "Debug", "net10.0-windows10.0.19041.0", "win-x64", "MostaqlK.exe")
            };

            foreach (var path in relativePaths)
            {
                var full = Path.GetFullPath(path);
                if (File.Exists(full))
                {
                    appPath = full;
                    break;
                }
            }
        }

        if (string.IsNullOrEmpty(appPath) || !File.Exists(appPath))
        {
            throw new FileNotFoundException("Could not locate MostaqlK.exe executable dynamically. Ensure the application is built before running UI tests.");
        }

        var options = new AppiumOptions();
        options.AddAdditionalCapability("app", Path.GetFullPath(appPath));
        options.AddAdditionalCapability("platformName", "Windows");
        options.AddAdditionalCapability("deviceName", "WindowsPC");

        // Connect to WinAppDriver running locally on port 4723
        Driver = new WindowsDriver<WindowsElement>(new Uri("http://127.0.0.1:4723"), options);
        Driver.Manage().Timeouts().ImplicitWait = TimeSpan.FromSeconds(5);
    }

    private static void StartWinAppDriver()
    {
        try
        {
            var winAppDriverPath = @"C:\Program Files (x86)\Windows Application Driver\WinAppDriver.exe";
            if (!File.Exists(winAppDriverPath))
            {
                winAppDriverPath = @"C:\Program Files\Windows Application Driver\WinAppDriver.exe";
            }

            if (File.Exists(winAppDriverPath))
            {
                _winAppDriverProcess = Process.Start(new ProcessStartInfo
                {
                    FileName = winAppDriverPath,
                    Arguments = "4723",
                    UseShellExecute = true,
                    CreateNoWindow = false
                });

                // Give it a couple of seconds to spin up and start listening on port 4723
                System.Threading.Thread.Sleep(2000);
            }
        }
        catch (Exception ex)
        {
            TestContext.WriteLine($"Warning: Could not automatically start WinAppDriver: {ex.Message}");
        }
    }

    [OneTimeTearDown]
    public void RunAfterAnyTests()
    {
        // 1. Gently close the driver / app session
        try
        {
            Driver?.Quit();
        }
        catch
        {
            // Ignore quit errors
        }

        // 2. Find and terminate any remaining MostaqlK instances (gentle close first, then force kill if needed)
        try
        {
            var processes = Process.GetProcessesByName("MostaqlK");
            foreach (var process in processes)
            {
                try
                {
                    // Try closing mainWindow gracefully if it has one
                    if (!process.HasExited)
                    {
                        process.CloseMainWindow();
                        // Wait up to 3 seconds for graceful exit
                        process.WaitForExit(3000);
                    }
                }
                catch
                {
                    // Ignore graceful close exception
                }

                // If still running, force kill
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill();
                        process.WaitForExit(1000);
                    }
                }
                catch
                {
                    // Ignore force kill exception
                }
                finally
                {
                    process.Dispose();
                }
            }
        }
        catch
        {
            // Ignore overall process cleanup errors
        }

        // 3. Terminate the automatically started WinAppDriver process
        try
        {
            if (_winAppDriverProcess != null && !_winAppDriverProcess.HasExited)
            {
                _winAppDriverProcess.Kill();
                _winAppDriverProcess.Dispose();
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }
}
