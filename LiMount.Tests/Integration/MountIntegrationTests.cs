using System.Diagnostics;
using System.Threading;
using FluentAssertions;
using LiMount.Core.Configuration;
using LiMount.Core.Interfaces;
using LiMount.Core.Models;
using LiMount.Core.Services;
using LiMount.Core.Services.Access;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LiMount.Tests.Integration;

/// <summary>
/// Integration tests for mounting real Linux drives.
/// These tests require:
/// - A physical Linux-formatted drive connected to the system
/// - WSL2 installed and configured
///
/// Set LIMOUNT_TEST_DISK_INDEX environment variable to specify the disk to test.
/// Tests are skipped if no test disk is configured or prerequisites are not met.
/// </summary>
[Collection("IntegrationTests")]
public class MountIntegrationTests : IAsyncLifetime
{
    private readonly ScriptExecutor _scriptExecutor;
    private readonly DiskEnumerationService _diskService;
    private readonly MountOrchestrator _mountOrchestrator;
    private readonly UnmountOrchestrator _unmountOrchestrator;
    private readonly IWindowsAccessService _windowsAccessService;
    private readonly IMountHistoryService _historyService;
    private readonly int? _testDiskIndex;
    private readonly int _testPartition;
    private readonly bool _requireHilExecution;
    private char? _mappedDriveLetter;

    public MountIntegrationTests()
    {
        var loggerFactory = NullLoggerFactory.Instance;
        var config = Options.Create(new LiMountConfiguration());

        // ScriptExecutor implements IMountScriptService, IDriveMappingService, and IFilesystemDetectionService
        _scriptExecutor = new ScriptExecutor(
            config,
            null,
            loggerFactory.CreateLogger<ScriptExecutor>());

        _diskService = new DiskEnumerationService(
            loggerFactory.CreateLogger<DiskEnumerationService>());

        _historyService = new MountHistoryService(
            loggerFactory.CreateLogger<MountHistoryService>(),
            config);

        _windowsAccessService = new WindowsAccessService(
            _scriptExecutor,
            loggerFactory.CreateLogger<WindowsAccessService>());

        _mountOrchestrator = new MountOrchestrator(
            _scriptExecutor, // IMountScriptService
            _windowsAccessService,
            config,
            _historyService);

        _unmountOrchestrator = new UnmountOrchestrator(
            _scriptExecutor, // IMountScriptService
            _windowsAccessService,
            _historyService);

        // Get test disk from environment variable or auto-detect
        var envDisk = Environment.GetEnvironmentVariable("LIMOUNT_TEST_DISK_INDEX");
        if (!string.IsNullOrEmpty(envDisk) && int.TryParse(envDisk, out var diskIndex))
        {
            _testDiskIndex = diskIndex;
        }
        else
        {
            // Auto-detect: find a non-system disk with Linux partitions
            var candidates = _diskService.GetCandidateDisks();
            var linuxDisk = candidates.FirstOrDefault(d => d.HasLinuxPartitions);
            _testDiskIndex = linuxDisk?.Index;
        }

        var envPartition = Environment.GetEnvironmentVariable("LIMOUNT_TEST_PARTITION");
        _testPartition = int.TryParse(envPartition, out var partition) && partition > 0
            ? partition
            : 1;

        _requireHilExecution = string.Equals(
            Environment.GetEnvironmentVariable("LIMOUNT_REQUIRE_HIL"),
            "1",
            StringComparison.OrdinalIgnoreCase);
    }

    public async Task InitializeAsync()
    {
        // Just unmount the specific disk if needed, don't shutdown WSL entirely
        if (_testDiskIndex.HasValue)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "wsl.exe",
                    Arguments = $"--unmount \\\\.\\PHYSICALDRIVE{_testDiskIndex.Value}",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using var process = Process.Start(psi);
                if (process != null)
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    try
                    {
                        await process.WaitForExitAsync(cts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        // Timed out; continue best-effort
                    }
                }
            }
            catch
            {
                // Ignore errors - disk may not be mounted
            }
        }

        await Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        // Clean up any mounted drives
        if (_mappedDriveLetter.HasValue && _testDiskIndex.HasValue)
        {
            try
            {
                await _unmountOrchestrator.UnmountAndUnmapAsync(_testDiskIndex.Value, WindowsAccessMode.DriveLetterLegacy, _mappedDriveLetter.Value);
            }
            catch
            {
                // Best effort cleanup - try shutdown
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "wsl.exe",
                        Arguments = "--shutdown",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    using var process = Process.Start(psi);
                    process?.WaitForExit(5000);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex);
                }
            }
        }
    }

    private bool CanRunTests(out string reason)
    {
        // Check if we have a test disk
        if (!_testDiskIndex.HasValue)
        {
            reason = "No test disk index resolved.";
            return false;
        }

        // Check if WSL is available
        try
        {
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = "wsl.exe",
                Arguments = "--status",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            process?.WaitForExit(5000);
            if (process?.ExitCode != 0)
            {
                reason = "wsl.exe --status failed.";
                return false;
            }

            reason = string.Empty;
            return true;
        }
        catch
        {
            reason = "Unable to execute wsl.exe --status.";
            return false;
        }
    }

    [Fact]
    public void DiskEnumeration_FindsTestDisk()
    {
        if (!_testDiskIndex.HasValue)
        {
            // Skip - no test disk configured
            return;
        }

        var disks = _diskService.GetDisks();
        var testDisk = disks.FirstOrDefault(d => d.Index == _testDiskIndex.Value);

        testDisk.Should().NotBeNull($"Test disk {_testDiskIndex} should exist");
        testDisk!.Partitions.Should().NotBeEmpty("Test disk should have partitions");
    }

    /// <summary>
    /// Comprehensive end-to-end test that mounts a drive, verifies access, lists contents, and unmounts.
    /// This single test covers the full workflow to avoid parallel execution issues.
    /// </summary>
    [Fact]
    public async Task FullMountWorkflow_MountsListsAndUnmounts()
    {
        if (!CanRunTests(out var skipReason))
        {
            if (_requireHilExecution)
            {
                throw new InvalidOperationException($"HIL execution was required but prerequisites were not met: {skipReason}");
            }

            Console.WriteLine($"Skipping: Prerequisites not met ({skipReason})");
            return;
        }

        Console.WriteLine($"Testing with disk index: {_testDiskIndex}, partition: {_testPartition}");

        // Step 1: Find a free drive letter
        var driveLetterService = new DriveLetterService();
        var freeLetters = driveLetterService.GetFreeLetters();
        var freeLetter = freeLetters.Count > 0 ? freeLetters[0] : '\0';
        freeLetter.Should().NotBe('\0', "Should have a free drive letter");
        Console.WriteLine($"Using drive letter: {freeLetter}:");

        // Step 2: Mount and map
        Console.WriteLine("Mounting...");
        var progress = new Progress<string>(msg => Console.WriteLine($"  {msg}"));
        var result = await _mountOrchestrator.MountAndMapAsync(_testDiskIndex!.Value, _testPartition, WindowsAccessMode.DriveLetterLegacy, freeLetter,
            "auto",
            null,
            progress);

        var expectXfsUnsupported = string.Equals(
            Environment.GetEnvironmentVariable("LIMOUNT_EXPECT_XFS_UNSUPPORTED"),
            "1",
            StringComparison.OrdinalIgnoreCase);

        if (expectXfsUnsupported)
        {
            result.Success.Should().BeFalse("Expected unsupported XFS mount failure for this HIL scenario");
            result.ErrorMessage.Should().Contain("XFS filesystem uses features unsupported by the current WSL kernel");
            (result.ErrorMessage?.Contains("XFS_UNSUPPORTED_FEATURES", StringComparison.OrdinalIgnoreCase) == true ||
             result.ErrorMessage?.Contains("Invalid argument", StringComparison.OrdinalIgnoreCase) == true)
                .Should().BeTrue("Expected the unsupported-XFS failure to include a diagnostic code or kernel mount error details");
            return;
        }

        result.Success.Should().BeTrue($"Mount should succeed. Error: {result.ErrorMessage}");
        result.DriveLetter.Should().Be(freeLetter);
        result.MountPathUNC.Should().NotBeNullOrEmpty("Should have UNC path");
        Console.WriteLine($"Mounted at: {result.MountPathUNC}");

        _mappedDriveLetter = freeLetter;
        var mountMarker = $"PHYSICALDRIVE{_testDiskIndex!.Value}p{_testPartition}";

        // Use UNC path for verification since elevated tests can't see user-context subst mappings
        var uncPath = result.MountPathUNC!;

        // Step 3: Verify UNC path is accessible
        Console.WriteLine("Verifying mount path exists...");
        Directory.Exists(uncPath).Should().BeTrue($"Mount path {uncPath} should exist");

        // Step 4: Test read access and list contents via UNC
        Console.WriteLine("Testing read access...");
        var entries = Directory.EnumerateFileSystemEntries(uncPath).ToList();
        entries.Should().NotBeNull("Should be able to enumerate contents");
        Console.WriteLine($"Mount {uncPath} contents ({entries.Count} items):");
        foreach (var entry in entries.Take(10))
        {
            Console.WriteLine($"  - {Path.GetFileName(entry)}");
        }
        if (entries.Count > 10)
        {
            Console.WriteLine($"  ... and {entries.Count - 10} more");
        }

        // Step 5: Verify WSL mount table includes this partition
        var mountTableAfterMount = await RunProcessForOutputAsync(
            "wsl.exe",
            "-e sh -lc \"mount | grep -i '/mnt/wsl/PHYSICALDRIVE' || true\"",
            TimeSpan.FromSeconds(10));
        mountTableAfterMount.Should().Contain(mountMarker,
            "WSL mount table should contain mounted partition marker after successful mount");

        // Step 6: Unmount
        Console.WriteLine("Unmounting...");
        var unmountResult = await _unmountOrchestrator.UnmountAndUnmapAsync(_testDiskIndex!.Value, WindowsAccessMode.DriveLetterLegacy, freeLetter);
        unmountResult.Success.Should().BeTrue($"Unmount should succeed. Error: {unmountResult.ErrorMessage}");

        // Step 7: Verify UNC path is gone (WSL mount removed)
        Console.WriteLine("Verifying drive is unmounted...");
        // Give WSL time to clean up
        await Task.Delay(500);
        var uncExistsAfterUnmount = Directory.Exists(uncPath);
        Console.WriteLine($"UNC path still exists after unmount: {uncExistsAfterUnmount}");

        var mountTableAfterUnmount = await RunProcessForOutputAsync(
            "wsl.exe",
            "-e sh -lc \"mount | grep -i '/mnt/wsl/PHYSICALDRIVE' || true\"",
            TimeSpan.FromSeconds(10));
        mountTableAfterUnmount.Should().NotContain(mountMarker,
            "WSL mount table should not contain partition marker after unmount");

        _mappedDriveLetter = null; // Cleanup done
        Console.WriteLine("Full workflow completed successfully!");
    }

    private static async Task<string> RunProcessForOutputAsync(string fileName, string arguments, TimeSpan timeout)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        using var cts = new CancellationTokenSource(timeout);

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cts.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(cts.Token);
        await process.WaitForExitAsync(cts.Token);

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        return string.IsNullOrWhiteSpace(stderr)
            ? stdout
            : $"{stdout}{Environment.NewLine}{stderr}";
    }
}
