using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Tools;
using FlaUI.UIA3;
using Xunit.Abstractions;

namespace LiMount.UITests;

public class MainPageUiTests
{
    private const string DiskComboBoxId = "DiskComboBox";
    private const string PartitionComboBoxId = "PartitionComboBox";
    private const string DriveLetterComboBoxId = "DriveLetterComboBox";
    private const string MountButtonId = "MountButton";
    private const string StatusTextBlockId = "StatusTextBlock";
    private const string VmFallbackMountedScreenshotFileName = "vm-fallback-mounted.png";
    private const string VmFallbackExplorerScreenshotFileName = "vm-fallback-explorer-network-share-open.png";

    private readonly ITestOutputHelper _output;

    public MainPageUiTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void XfsUnsupportedScenario_ShowsActionableStatusMessage()
    {
        if (!ShouldRunUiTests())
        {
            _output.WriteLine("Skipping UI test. Set LIMOUNT_RUN_UI_TESTS=1 to enable.");
            return;
        }

        var launched = LaunchAppForScenario("xfs_unsupported");
        var status = RunMountFlowAndReadStatus(
            launched.Application,
            launched.Automation,
            text => text.Contains("unsupported by the current WSL kernel", StringComparison.OrdinalIgnoreCase),
            "xfs_unsupported");

        Assert.Contains("unsupported by the current WSL kernel", status, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("XFS_UNSUPPORTED_FEATURES", status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SuccessScenario_ShowsSuccessStatusMessage()
    {
        if (!ShouldRunUiTests())
        {
            _output.WriteLine("Skipping UI test. Set LIMOUNT_RUN_UI_TESTS=1 to enable.");
            return;
        }

        var launched = LaunchAppForScenario("success");
        var status = RunMountFlowAndReadStatus(
            launched.Application,
            launched.Automation,
            text => text.Contains("Success!", StringComparison.OrdinalIgnoreCase),
            "success");

        Assert.Contains("Success!", status, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("failed", status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ForcedWizardScenario_CanContinueAndMount()
    {
        if (!ShouldRunUiTests())
        {
            _output.WriteLine("Skipping UI test. Set LIMOUNT_RUN_UI_TESTS=1 to enable.");
            return;
        }

        var launched = LaunchAppForScenario("success", forceWizard: true);
        var status = RunMountFlowAndReadStatus(
            launched.Application,
            launched.Automation,
            text => text.Contains("Success!", StringComparison.OrdinalIgnoreCase),
            "forced_wizard");

        Assert.Contains("Success!", status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(Skip = "Deferred until VM fallback is complete (CO1) and HIL VM fallback verification is in place (HIL1).")]
    public void VmFallbackE2e_CapturesMountedAndExplorerNetworkShareScreenshots()
    {
        // Deferred acceptance sequence once VM fallback is implemented:
        // 1) Force fallback path: WSL incompatibility -> VM fallback success.
        // 2) Verify the app shows mounted state for the fallback-backed partition.
        // 3) Invoke Open in Explorer for the network share target.
        // 4) Capture app + Explorer screenshots via vendored scripts/take_screenshot.ps1.
        //
        // Required output convention:
        // screenshots/ui-batch/<timestamp>/vm-fallback-mounted.png
        // screenshots/ui-batch/<timestamp>/vm-fallback-explorer-network-share-open.png
        _output.WriteLine(
            $"Reserved VM fallback screenshot filenames: {VmFallbackMountedScreenshotFileName}, {VmFallbackExplorerScreenshotFileName}.");
    }

    private static bool ShouldRunUiTests()
    {
        return string.Equals(
            Environment.GetEnvironmentVariable("LIMOUNT_RUN_UI_TESTS"),
            "1",
            StringComparison.OrdinalIgnoreCase);
    }

    private (Application Application, UIA3Automation Automation) LaunchAppForScenario(string scenario, bool forceWizard = false)
    {
        var executablePath = GetWinUiExecutablePath();
        if (!File.Exists(executablePath))
        {
            throw new FileNotFoundException(
                $"Could not find WinUI executable at '{executablePath}'. Build LiMount.WinUI first.");
        }

        var startInfo = new ProcessStartInfo(executablePath)
        {
            UseShellExecute = false
        };
        startInfo.Environment["LIMOUNT_TEST_MODE"] = "1";
        startInfo.Environment["LIMOUNT_TEST_SCENARIO"] = scenario;
        startInfo.Environment["LIMOUNT_TEST_FORCE_WIZARD"] = forceWizard ? "1" : "0";

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to launch WinUI app process for UI test.");

        var app = Application.Attach(process);
        var automation = new UIA3Automation();

        return (app, automation);
    }

    private static string GetWinUiExecutablePath()
    {
        return Path.Combine(
            GetSolutionRootPath(),
            "LiMount.WinUI",
            "bin", "x64", "Debug",
            "net10.0-windows10.0.26100.0",
            "win-x64",
            "LiMount.WinUI.exe");
    }

    private static string GetScreenshotScriptPath()
    {
        return Path.Combine(GetSolutionRootPath(), "scripts", "take_screenshot.ps1");
    }

    private static string GetSolutionRootPath()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null && !File.Exists(Path.Combine(current.FullName, "LiMount.sln")))
        {
            current = current.Parent;
        }

        if (current == null)
        {
            throw new DirectoryNotFoundException("Unable to locate solution root containing LiMount.sln.");
        }

        return current.FullName;
    }

    private string RunMountFlowAndReadStatus(
        Application app,
        UIA3Automation automation,
        Func<string, bool> isExpectedStatus,
        string scenarioName)
    {
        try
        {
            var window = Retry.WhileNull(
                () => app.GetMainWindow(automation),
                timeout: TimeSpan.FromSeconds(30),
                throwOnTimeout: true).Result
                ?? throw new InvalidOperationException("Main window was not found.");

            CompleteSetupWizardIfPresent(window);

            WaitForComboToHaveItems(window, DiskComboBoxId, "disk");

            SelectFirstComboItem(window, DiskComboBoxId, "disk");
            WaitForComboToHaveItems(window, PartitionComboBoxId, "partition");
            SelectFirstComboItem(window, PartitionComboBoxId, "partition");

            if (TryGetVisibleComboBox(window, DriveLetterComboBoxId, out _))
            {
                WaitForComboToHaveItems(window, DriveLetterComboBoxId, "drive letter");
                SelectFirstComboItem(window, DriveLetterComboBoxId, "drive letter");
            }

            var mountButton = GetButton(window, MountButtonId);
            Retry.WhileTrue(
                () => !mountButton.IsEnabled,
                timeout: TimeSpan.FromSeconds(20),
                throwOnTimeout: true,
                timeoutMessage: "Mount button did not become enabled.");

            mountButton.Invoke();

            var resolvedStatus = WaitForExpectedStatus(window, isExpectedStatus, scenarioName);
            _output.WriteLine($"Scenario '{scenarioName}' status: {resolvedStatus}");
            TryCaptureScreenshot(window, scenarioName);
            return resolvedStatus;
        }
        finally
        {
            try
            {
                app.Close();
            }
            catch
            {
                // Best effort shutdown for UI automation.
            }

            automation.Dispose();
        }
    }

    private void CompleteSetupWizardIfPresent(Window window)
    {
        var wizardSaveButton = Retry.WhileNull(
            () => window.FindFirstDescendant(cf => cf.ByName("Save and Continue"))?.AsButton(),
            timeout: TimeSpan.FromSeconds(8)).Result;

        if (wizardSaveButton == null)
        {
            return;
        }

        _output.WriteLine("Setup wizard detected in UI test. Completing it.");
        wizardSaveButton.Invoke();
    }

    private static Button GetButton(Window window, string automationId)
    {
        return window.FindFirstDescendant(cf => cf.ByAutomationId(automationId))?.AsButton()
            ?? throw new InvalidOperationException($"Button '{automationId}' was not found.");
    }

    private static ComboBox GetComboBox(Window window, string automationId)
    {
        return window.FindFirstDescendant(cf => cf.ByAutomationId(automationId))?.AsComboBox()
            ?? throw new InvalidOperationException($"ComboBox '{automationId}' was not found.");
    }

    private static bool TryGetVisibleComboBox(Window window, string automationId, out ComboBox? comboBox)
    {
        comboBox = window.FindFirstDescendant(cf => cf.ByAutomationId(automationId))?.AsComboBox();
        if (comboBox == null)
        {
            return false;
        }

        return !comboBox.IsOffscreen;
    }

    private static void WaitForComboToHaveItems(Window window, string automationId, string displayName)
    {
        Retry.WhileTrue(
            () =>
            {
                try
                {
                    var combo = GetComboBox(window, automationId);
                    return combo.Items.Length == 0;
                }
                catch
                {
                    return true;
                }
            },
            timeout: TimeSpan.FromSeconds(25),
            throwOnTimeout: true,
            timeoutMessage: $"The {displayName} combo box did not populate.");
    }

    private static void SelectFirstComboItem(Window window, string automationId, string displayName)
    {
        var combo = GetComboBox(window, automationId);
        if (combo.Items.Length == 0)
        {
            throw new InvalidOperationException($"The {displayName} combo box has no items to select.");
        }

        combo.Select(0);
    }

    private static string ReadStatusText(Window window)
    {
        var label = window.FindFirstDescendant(cf => cf.ByAutomationId(StatusTextBlockId))?.AsLabel();
        return label?.Text ?? string.Empty;
    }

    private string WaitForExpectedStatus(
        Window window,
        Func<string, bool> isExpectedStatus,
        string scenarioName)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var timeoutAt = DateTime.UtcNow.AddSeconds(25);

        while (DateTime.UtcNow < timeoutAt)
        {
            var status = ReadStatusText(window).Trim();
            if (!string.IsNullOrWhiteSpace(status))
            {
                if (seen.Add(status))
                {
                    _output.WriteLine($"[{scenarioName}] status: {status}");
                }

                if (isExpectedStatus(status))
                {
                    return status;
                }
            }

            Thread.Sleep(250);
        }

        TryCaptureScreenshot(window, $"{scenarioName}-timeout");
        var observed = seen.Count == 0 ? "(none)" : string.Join(" | ", seen);
        throw new TimeoutException($"Expected status was not observed. Seen statuses: {observed}");
    }

    private void TryCaptureScreenshot(Window window, string scenarioName)
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("LIMOUNT_CAPTURE_SCREENSHOT"), "1", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var screenshotScriptPath = GetScreenshotScriptPath();
        if (!File.Exists(screenshotScriptPath))
        {
            _output.WriteLine($"Screenshot helper script not found at '{screenshotScriptPath}'.");
            return;
        }

        try
        {
            var nativeHandle = window.Properties.NativeWindowHandle.ValueOrDefault;
            var captureArgs = BuildScreenshotCaptureArgs(window, nativeHandle);
            var psi = new ProcessStartInfo("powershell",
                $"-ExecutionPolicy Bypass -File \"{screenshotScriptPath}\" -Mode temp {captureArgs}")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null)
            {
                _output.WriteLine("Failed to launch screenshot skill helper.");
                return;
            }

            var output = process.StandardOutput.ReadToEnd().Trim();
            var error = process.StandardError.ReadToEnd().Trim();
            process.WaitForExit(10000);

            if (!string.IsNullOrWhiteSpace(output))
            {
                _output.WriteLine($"Screenshot ({scenarioName}): {output}");
            }

            if (!string.IsNullOrWhiteSpace(error))
            {
                _output.WriteLine($"Screenshot helper stderr ({scenarioName}): {error}");
            }
        }
        catch (Exception ex)
        {
            _output.WriteLine($"Screenshot capture failed for scenario '{scenarioName}': {ex.Message}");
        }
    }

    private static string BuildScreenshotCaptureArgs(Window window, nint nativeHandle)
    {
        if (nativeHandle != 0)
        {
            return "-WindowHandle " + nativeHandle.ToString(CultureInfo.InvariantCulture);
        }

        var bounds = window.BoundingRectangle;
        if (bounds.Width > 0 && bounds.Height > 0)
        {
            return $"-Region {bounds.Left},{bounds.Top},{bounds.Width},{bounds.Height}";
        }

        return "-ActiveWindow";
    }
}
