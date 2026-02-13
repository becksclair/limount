using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.Tools;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;
using Xunit.Abstractions;

namespace LiMount.UITests;

public class ScreenshotBatchUiTests
{
    private const string DiskComboBoxId = "DiskComboBox";
    private const string PartitionComboBoxId = "PartitionComboBox";
    private const string DriveLetterComboBoxId = "DriveLetterComboBox";
    private const string WizardBackendComboBoxId = "BackendPreferenceCombo";
    private const string WizardFallbackComboBoxId = "FallbackPolicyCombo";
    private const string WizardAccessModeComboBoxId = "AccessModeCombo";
    private const string WizardSaveButtonName = "Save and Continue";
    private const string WizardTestButtonName = "Test configuration";

    private static readonly string[] ExpectedBatchFiles =
    [
        "01-main-initial.png",
        "02-main-disk-dropdown-open.png",
        "03-main-partition-dropdown-open.png",
        "04-main-drive-letter-dropdown-open.png",
        "05-wizard-top.png",
        "06-wizard-middle.png",
        "07-wizard-bottom.png",
        "08-wizard-backend-dropdown-open.png",
        "09-wizard-fallback-dropdown-open.png",
        "10-wizard-access-mode-dropdown-open.png",
        "11-wizard-after-test-configuration.png",
        "12-main-after-wizard.png"
    ];

    private readonly ITestOutputHelper _output;

    public ScreenshotBatchUiTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void CaptureImplementedUiScreenshotsBatch()
    {
        if (!ShouldRunUiTests())
        {
            _output.WriteLine("Skipping batch screenshots. Set LIMOUNT_RUN_UI_TESTS=1 to enable UI tests.");
            return;
        }

        if (!ShouldCaptureScreenshotBatch())
        {
            _output.WriteLine("Skipping batch screenshots. Set LIMOUNT_CAPTURE_SCREENSHOT_BATCH=1 to enable.");
            return;
        }

        var screenshotScriptPath = GetScreenshotScriptPath();
        if (!File.Exists(screenshotScriptPath))
        {
            throw new FileNotFoundException(
                $"Screenshot helper script was not found at '{screenshotScriptPath}'.");
        }

        var outputDirectory = CreateBatchOutputDirectory();
        _output.WriteLine($"Writing UI screenshot batch to: {outputDirectory}");

        CaptureMainPageScreenshots(outputDirectory, screenshotScriptPath);
        CaptureWizardScreenshots(outputDirectory, screenshotScriptPath);

        foreach (var fileName in ExpectedBatchFiles)
        {
            var path = Path.Combine(outputDirectory, fileName);
            Assert.True(File.Exists(path), $"Expected screenshot file was not created: {path}");

            var fileInfo = new FileInfo(path);
            Assert.True(fileInfo.Length > 0, $"Screenshot file is empty: {path}");
        }
    }

    private void CaptureMainPageScreenshots(string outputDirectory, string screenshotScriptPath)
    {
        var launched = LaunchAppForScenario("success");
        try
        {
            var window = GetMainWindow(launched.Application, launched.Automation);
            EnsureWindowHasRoomForFormCaptures(window);
            WaitForComboToHaveItems(window, DiskComboBoxId, "disk");
            var driveLetterVisible = TryGetVisibleComboBox(window, DriveLetterComboBoxId, out _);
            if (driveLetterVisible)
            {
                WaitForComboToHaveItems(window, DriveLetterComboBoxId, "drive letter");
            }
            EnsureCombosCollapsed(window, DiskComboBoxId, PartitionComboBoxId, DriveLetterComboBoxId);

            CaptureScreenshot(window, screenshotScriptPath, Path.Combine(outputDirectory, "01-main-initial.png"));

            ExpandComboAndCapture(
                window,
                DiskComboBoxId,
                screenshotScriptPath,
                Path.Combine(outputDirectory, "02-main-disk-dropdown-open.png"));

            SelectFirstComboItem(window, DiskComboBoxId, "disk");
            WaitForComboToHaveItems(window, PartitionComboBoxId, "partition");

            ExpandComboAndCapture(
                window,
                PartitionComboBoxId,
                screenshotScriptPath,
                Path.Combine(outputDirectory, "03-main-partition-dropdown-open.png"));

            if (driveLetterVisible)
            {
                ExpandComboAndCapture(
                    window,
                    DriveLetterComboBoxId,
                    screenshotScriptPath,
                    Path.Combine(outputDirectory, "04-main-drive-letter-dropdown-open.png"));
            }
            else
            {
                // Default network-location mode hides the drive letter picker; keep deterministic filename.
                CaptureScreenshot(window, screenshotScriptPath, Path.Combine(outputDirectory, "04-main-drive-letter-dropdown-open.png"));
            }

            EnsureCombosCollapsed(window, DiskComboBoxId, PartitionComboBoxId, DriveLetterComboBoxId);
        }
        finally
        {
            CloseLaunchedApp(launched);
        }
    }

    private void CaptureWizardScreenshots(string outputDirectory, string screenshotScriptPath)
    {
        var launched = LaunchAppForScenario("success", forceWizard: true);
        try
        {
            var window = GetMainWindow(launched.Application, launched.Automation);
            EnsureWindowHasRoomForFormCaptures(window);
            WaitForWizardSaveButton(window);

            CaptureScreenshot(window, screenshotScriptPath, Path.Combine(outputDirectory, "05-wizard-top.png"));

            ScrollWizardForScreenshot(window);
            CaptureScreenshot(window, screenshotScriptPath, Path.Combine(outputDirectory, "06-wizard-middle.png"));

            ScrollWizardForScreenshot(window);
            CaptureScreenshot(window, screenshotScriptPath, Path.Combine(outputDirectory, "07-wizard-bottom.png"));

            ExpandComboAndCapture(
                window,
                WizardBackendComboBoxId,
                screenshotScriptPath,
                Path.Combine(outputDirectory, "08-wizard-backend-dropdown-open.png"));

            ExpandComboAndCapture(
                window,
                WizardFallbackComboBoxId,
                screenshotScriptPath,
                Path.Combine(outputDirectory, "09-wizard-fallback-dropdown-open.png"));

            ExpandComboAndCapture(
                window,
                WizardAccessModeComboBoxId,
                screenshotScriptPath,
                Path.Combine(outputDirectory, "10-wizard-access-mode-dropdown-open.png"));

            var testConfigurationButton = window.FindFirstDescendant(cf => cf.ByName(WizardTestButtonName))?.AsButton()
                ?? throw new InvalidOperationException("Could not find setup wizard 'Test configuration' button.");
            testConfigurationButton.Invoke();
            Thread.Sleep(400);
            CaptureScreenshot(window, screenshotScriptPath, Path.Combine(outputDirectory, "11-wizard-after-test-configuration.png"));

            var saveButton = WaitForWizardSaveButton(window);
            saveButton.Invoke();

            Retry.WhileTrue(
                () => window.FindFirstDescendant(cf => cf.ByName(WizardSaveButtonName)) != null,
                timeout: TimeSpan.FromSeconds(10),
                throwOnTimeout: true,
                timeoutMessage: "Setup wizard did not close after clicking Save and Continue.");

            WaitForComboToHaveItems(window, DiskComboBoxId, "disk");
            EnsureCombosCollapsed(window, DiskComboBoxId, PartitionComboBoxId, DriveLetterComboBoxId);
            CaptureScreenshot(window, screenshotScriptPath, Path.Combine(outputDirectory, "12-main-after-wizard.png"));
        }
        finally
        {
            CloseLaunchedApp(launched);
        }
    }

    private static bool ShouldRunUiTests()
    {
        return string.Equals(
            Environment.GetEnvironmentVariable("LIMOUNT_RUN_UI_TESTS"),
            "1",
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldCaptureScreenshotBatch()
    {
        return string.Equals(
            Environment.GetEnvironmentVariable("LIMOUNT_CAPTURE_SCREENSHOT_BATCH"),
            "1",
            StringComparison.OrdinalIgnoreCase);
    }

    private static string CreateBatchOutputDirectory()
    {
        var root = GetSolutionRootPath();
        var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var directory = Path.Combine(root, "screenshots", "ui-batch", timestamp);
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static string GetScreenshotScriptPath()
    {
        return Path.Combine(GetSolutionRootPath(), "scripts", "take_screenshot.ps1");
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

    private static (Application Application, UIA3Automation Automation) LaunchAppForScenario(string scenario, bool forceWizard = false)
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

    private static void CloseLaunchedApp((Application Application, UIA3Automation Automation) launched)
    {
        try
        {
            launched.Application.Close();
        }
        catch
        {
            // Best effort shutdown for UI automation.
        }
        finally
        {
            launched.Automation.Dispose();
        }
    }

    private static Window GetMainWindow(Application app, UIA3Automation automation)
    {
        return Retry.WhileNull(
            () => app.GetMainWindow(automation),
            timeout: TimeSpan.FromSeconds(30),
            throwOnTimeout: true).Result
            ?? throw new InvalidOperationException("Main window was not found.");
    }

    private static Button WaitForWizardSaveButton(Window window)
    {
        return Retry.WhileNull(
            () => window.FindFirstDescendant(cf => cf.ByName(WizardSaveButtonName))?.AsButton(),
            timeout: TimeSpan.FromSeconds(20),
            throwOnTimeout: true,
            timeoutMessage: "Setup wizard Save and Continue button was not found.").Result
            ?? throw new InvalidOperationException("Setup wizard Save and Continue button was not found.");
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

    private static void ExpandComboAndCapture(
        Window window,
        string automationId,
        string screenshotScriptPath,
        string outputPath)
    {
        var combo = GetComboBox(window, automationId);
        combo.Focus();
        Thread.Sleep(200);
        combo.Expand();
        Thread.Sleep(350);
        var popupCaptureArgs = TryBuildPopupCaptureArgs(window);
        CaptureScreenshot(
            window,
            screenshotScriptPath,
            outputPath,
            preferActiveWindowCapture: popupCaptureArgs == null,
            explicitCaptureArgs: popupCaptureArgs);
        combo.Collapse();
        Thread.Sleep(200);
    }

    private static void EnsureCombosCollapsed(Window window, params string[] automationIds)
    {
        foreach (var automationId in automationIds)
        {
            try
            {
                var combo = GetComboBox(window, automationId);
                combo.Collapse();
            }
            catch
            {
                // Best effort cleanup before baseline captures.
            }
        }
    }

    private static void ScrollWizardForScreenshot(Window window)
    {
        window.Focus();
        Keyboard.Type(VirtualKeyShort.NEXT);
        Thread.Sleep(450);
    }

    private static void CaptureScreenshot(
        Window window,
        string screenshotScriptPath,
        string outputPath,
        bool preferActiveWindowCapture = false,
        string? explicitCaptureArgs = null)
    {
        var initialCaptureArgs = explicitCaptureArgs
            ?? (preferActiveWindowCapture
                ? "-ActiveWindow"
                : BuildScreenshotCaptureArgs(window, window.Properties.NativeWindowHandle.ValueOrDefault));

        var fallbackMainWindowArgs = BuildScreenshotCaptureArgs(window, window.Properties.NativeWindowHandle.ValueOrDefault);
        var attempts = new[] { initialCaptureArgs, "-ActiveWindow", fallbackMainWindowArgs }
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var lastError = string.Empty;
        foreach (var args in attempts)
        {
            if (TryRunScreenshotHelper(screenshotScriptPath, outputPath, args, out lastError))
            {
                return;
            }
        }

        throw new InvalidOperationException(lastError);
    }

    private static bool TryRunScreenshotHelper(
        string screenshotScriptPath,
        string outputPath,
        string captureArgs,
        out string errorMessage)
    {
        var psi = new ProcessStartInfo("powershell",
            $"-ExecutionPolicy Bypass -File \"{screenshotScriptPath}\" -Path \"{outputPath}\" {captureArgs}")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to launch screenshot helper process.");

        var standardOutput = process.StandardOutput.ReadToEnd().Trim();
        var standardError = process.StandardError.ReadToEnd().Trim();
        process.WaitForExit(10000);

        if (process.ExitCode != 0)
        {
            errorMessage = $"Screenshot helper failed with exit code {process.ExitCode}. stderr: {standardError}";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(standardOutput) && !File.Exists(outputPath))
        {
            errorMessage = $"Screenshot helper returned '{standardOutput}', but expected output '{outputPath}' was not found.";
            return false;
        }

        errorMessage = string.Empty;
        return true;
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

    private static string? TryBuildPopupCaptureArgs(Window window)
    {
        var popup = Retry.WhileNull(
            () => window.Popup,
            timeout: TimeSpan.FromSeconds(2)).Result;
        if (popup == null)
        {
            return null;
        }

        var popupHandle = popup.Properties.NativeWindowHandle.ValueOrDefault;
        return popupHandle != 0
            ? "-WindowHandle " + popupHandle.ToString(CultureInfo.InvariantCulture)
            : null;
    }

    private static void EnsureWindowHasRoomForFormCaptures(Window window)
    {
        var nativeHandle = window.Properties.NativeWindowHandle.ValueOrDefault;
        if (nativeHandle == 0)
        {
            return;
        }

        var handle = (IntPtr)nativeHandle;
        NativeMethods.ShowWindow(handle, NativeMethods.SW_RESTORE);
        NativeMethods.SetWindowPos(
            handle,
            IntPtr.Zero,
            60,
            40,
            1500,
            1100,
            NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
        Thread.Sleep(250);
    }

    private static class NativeMethods
    {
        internal const int SW_RESTORE = 9;
        internal const uint SWP_NOZORDER = 0x0004;
        internal const uint SWP_NOACTIVATE = 0x0010;

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetWindowPos(
            IntPtr hWnd,
            IntPtr hWndInsertAfter,
            int x,
            int y,
            int cx,
            int cy,
            uint uFlags);
    }
}
