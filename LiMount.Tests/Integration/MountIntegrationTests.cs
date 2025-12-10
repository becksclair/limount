using System.Diagnostics;
using FluentAssertions;
using LiMount.Core.Configuration;
using LiMount.Core.Interfaces;
using LiMount.Core.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LiMount.Tests.Integration;

/// <summary>
/// Integration tests for mounting real Linux drives.
/// These tests require:
/// - A physical Linux-formatted drive connected to the system
/// - Administrator privileges
/// - WSL2 installed and configured
///
/// Set LIMOUNT_TEST_DISK_INDEX environment variable to specify the disk to test.
/// Tests are skipped if no test disk is configured or prerequisites are not met.
/// </summary>
[Collection("IntegrationTests")]
public class MountIntegrationTests : IAsyncLifetime
{
    private readonly ScriptExecutor _scriptExecutor;
    private readonly IDiskEnumerationService _diskService;
    private readonly IMountOrchestrator _mountOrchestrator;
    private readonly IUnmountOrchestrator _unmountOrchestrator;
    private readonly IMountHistoryService _historyService;
    private readonly int? _testDiskIndex;
    private readonly int _testPartition = 1;
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

        _mountOrchestrator = new MountOrchestrator(
            _scriptExecutor, // IMountScriptService
            _scriptExecutor, // IDriveMappingService
            config,
            _historyService);

        _unmountOrchestrator = new UnmountOrchestrator(
            _scriptExecutor, // IMountScriptService
            _scriptExecutor, // IDriveMappingService
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
                process?.WaitForExit(5000);
            }
            catch
            {
                // Ignore errors - disk may not be mounted
            }
        }
    }

    public async Task DisposeAsync()
    {
        // Clean up any mounted drives
        if (_mappedDriveLetter.HasValue && _testDiskIndex.HasValue)
        {
            try
            {
                await _unmountOrchestrator.UnmountAndUnmapAsync(
                    _testDiskIndex.Value,
                    _mappedDriveLetter.Value);
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
                catch { }
            }
        }
    }

    private bool CanRunTests()
    {
        // Check if we have a test disk
        if (!_testDiskIndex.HasValue)
            return false;

        // Check if running as admin
        var principal = new System.Security.Principal.WindowsPrincipal(
            System.Security.Principal.WindowsIdentity.GetCurrent());
        if (!principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator))
            return false;

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
            return process?.ExitCode == 0;
        }
        catch
        {
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
        if (!CanRunTests())
        {
            Console.WriteLine("Skipping: Prerequisites not met (need admin + WSL + test disk)");
            return;
        }

        Console.WriteLine($"Testing with disk index: {_testDiskIndex}");

        // Step 1: Find a free drive letter
        var driveLetterService = new DriveLetterService();
        var freeLetter = driveLetterService.GetFreeLetters().FirstOrDefault();
        freeLetter.Should().NotBe('\0', "Should have a free drive letter");
        Console.WriteLine($"Using drive letter: {freeLetter}:");

        // Step 2: Mount and map
        Console.WriteLine("Mounting...");
        var progress = new Progress<string>(msg => Console.WriteLine($"  {msg}"));
        var result = await _mountOrchestrator.MountAndMapAsync(
            _testDiskIndex!.Value,
            _testPartition,
            freeLetter,
            "auto",
            null,
            progress);

        result.Success.Should().BeTrue($"Mount should succeed. Error: {result.ErrorMessage}");
        result.DriveLetter.Should().Be(freeLetter);
        result.MountPathUNC.Should().NotBeNullOrEmpty("Should have UNC path");
        Console.WriteLine($"Mounted at: {result.MountPathUNC}");

        _mappedDriveLetter = freeLetter;
        
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

        // Step 5: Unmount
        Console.WriteLine("Unmounting...");
        var unmountResult = await _unmountOrchestrator.UnmountAndUnmapAsync(
            _testDiskIndex!.Value,
            freeLetter);
        unmountResult.Success.Should().BeTrue($"Unmount should succeed. Error: {unmountResult.ErrorMessage}");

        // Step 6: Verify UNC path is gone (WSL mount removed)
        Console.WriteLine("Verifying drive is unmounted...");
        // Give WSL time to clean up
        await Task.Delay(500);
        Directory.Exists(uncPath).Should().BeFalse("UNC path should not exist after unmount");

        _mappedDriveLetter = null; // Cleanup done
        Console.WriteLine("Full workflow completed successfully!");
    }
}
