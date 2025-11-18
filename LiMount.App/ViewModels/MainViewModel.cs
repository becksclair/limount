using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiMount.Core.Interfaces;
using LiMount.Core.Models;
using LiMount.Core.Services;

namespace LiMount.App.ViewModels;

/// <summary>
/// Main ViewModel for the LiMount application.
/// Handles disk enumeration, partition selection, and mounting operations.
/// Uses CommunityToolkit.Mvvm for MVVM pattern support.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly IDiskEnumerationService _diskService;
    private readonly IDriveLetterService _driveLetterService;

    [ObservableProperty]
    private ObservableCollection<DiskInfo> _disks = new();

    [ObservableProperty]
    private DiskInfo? _selectedDisk;

    [ObservableProperty]
    private ObservableCollection<PartitionInfo> _partitions = new();

    [ObservableProperty]
    private PartitionInfo? _selectedPartition;

    [ObservableProperty]
    private ObservableCollection<char> _freeDriveLetters = new();

    [ObservableProperty]
    private char? _selectedDriveLetter;

    [ObservableProperty]
    private string _selectedFsType = "ext4";

    [ObservableProperty]
    private string _statusMessage = "Ready. Select a disk, partition, and drive letter, then click Mount.";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _canOpenExplorer;

    private string? _lastMappedDriveLetter;

    public MainViewModel(IDiskEnumerationService diskService, IDriveLetterService driveLetterService)
    {
        _diskService = diskService;
        _driveLetterService = driveLetterService;

        // Subscribe to SelectedDisk changes to update partitions
        PropertyChanged += OnPropertyChanged;
    }

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SelectedDisk))
        {
            UpdatePartitions();
        }
    }

    /// <summary>
    /// Loads disks and drive letters from the system.
    /// </summary>
    [RelayCommand]
    private void Refresh()
    {
        LoadDisksAndDriveLetters();
    }

    /// <summary>
    /// Initializes the ViewModel by loading disks and drive letters asynchronously.
    /// Call this after instantiation to perform heavy I/O operations.
    /// </summary>
    public async Task InitializeAsync()
    {
        await Task.Run(() => LoadDisksAndDriveLetters());
    }

    private void LoadDisksAndDriveLetters()
    {
        try
        {
            StatusMessage = "Loading disks and drive letters...";

            // Get candidate disks (non-system, non-boot)
            var candidateDisks = _diskService.GetCandidateDisks();

            Disks.Clear();
            foreach (var disk in candidateDisks)
            {
                Disks.Add(disk);
            }

            // Get free drive letters (sorted Z→A)
            var freeLetters = _driveLetterService.GetFreeLetters();

            FreeDriveLetters.Clear();
            foreach (var letter in freeLetters)
            {
                FreeDriveLetters.Add(letter);
            }

            // Auto-select first disk and drive letter if available
            if (Disks.Count > 0 && SelectedDisk == null)
            {
                SelectedDisk = Disks[0];
            }

            if (FreeDriveLetters.Count > 0 && SelectedDriveLetter == null)
            {
                SelectedDriveLetter = FreeDriveLetters[0];
            }

            StatusMessage = $"Found {Disks.Count} candidate disk(s) and {FreeDriveLetters.Count} free drive letter(s).";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error loading disks: {ex.Message}";
        }
    }

    private void UpdatePartitions()
    {
        Partitions.Clear();

        if (SelectedDisk == null)
        {
            return;
        }

        // Filter partitions to only show likely Linux partitions
        var linuxPartitions = SelectedDisk.Partitions
            .Where(p => p.IsLikelyLinux)
            .ToList();

        foreach (var partition in linuxPartitions)
        {
            Partitions.Add(partition);
        }

        // Auto-select first partition if available
        if (Partitions.Count > 0)
        {
            SelectedPartition = Partitions[0];
        }
        else
        {
            SelectedPartition = null;
        }
    }

    /// <summary>
    /// Mounts the selected disk partition into WSL2 and maps it to a Windows drive letter.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanMount))]
    private async Task MountAsync()
    {
        if (SelectedDisk == null || SelectedPartition == null || SelectedDriveLetter == null)
        {
            StatusMessage = "Please select a disk, partition, and drive letter.";
            return;
        }

        IsBusy = true;
        CanOpenExplorer = false;

        try
        {
            // Step 1: Mount disk in WSL using elevated PowerShell script
            StatusMessage = $"Mounting disk {SelectedDisk.Index} partition {SelectedPartition.PartitionNumber} in WSL...";

            var mountResult = await ExecuteMountScriptAsync(
                SelectedDisk.Index,
                SelectedPartition.PartitionNumber,
                SelectedFsType);

            if (!mountResult.Success)
            {
                StatusMessage = $"Mount failed: {mountResult.ErrorMessage}";
                return;
            }

            StatusMessage = $"Mounted successfully. WSL path: {mountResult.MountPathLinux}";

            // Step 2: Map WSL UNC path to drive letter
            StatusMessage = $"Mapping {mountResult.MountPathUNC} to {SelectedDriveLetter}:...";

            var mappingResult = await ExecuteMappingScriptAsync(
                SelectedDriveLetter.Value,
                mountResult.MountPathUNC!);

            if (!mappingResult.Success)
            {
                StatusMessage = $"Mapping failed: {mappingResult.ErrorMessage}";
                return;
            }

            // Success!
            _lastMappedDriveLetter = SelectedDriveLetter.ToString();
            CanOpenExplorer = true;

            StatusMessage = $"Success! Mounted as {SelectedDriveLetter}: - You can now access the Linux partition from Windows Explorer.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanMount()
    {
        return !IsBusy &&
               SelectedDisk != null &&
               SelectedPartition != null &&
               SelectedDriveLetter != null;
    }

    /// <summary>
    /// Opens Windows Explorer to the mapped drive letter.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanOpenExplorerExecute))]
    private void OpenExplorer()
    {
        if (string.IsNullOrEmpty(_lastMappedDriveLetter))
        {
            return;
        }

        try
        {
            var drivePath = $"{_lastMappedDriveLetter}:\\";
            Process.Start("explorer.exe", drivePath);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to open Explorer: {ex.Message}";
        }
    }

    private bool CanOpenExplorerExecute()
    {
        return CanOpenExplorer && !string.IsNullOrEmpty(_lastMappedDriveLetter);
    }

    /// <summary>
    /// Executes the Mount-LinuxDiskCore.ps1 script with elevation.
    /// </summary>
    private async Task<MountResult> ExecuteMountScriptAsync(int diskIndex, int partition, string fsType)
    {
        var scriptPath = GetScriptPath("Mount-LinuxDiskCore.ps1");

        if (!File.Exists(scriptPath))
        {
            return new MountResult
            {
                Success = false,
                ErrorMessage = $"Mount script not found at: {scriptPath}"
            };
        }

        var arguments = $"-ExecutionPolicy Bypass -File \"{scriptPath}\" " +
                       $"-DiskIndex {diskIndex} " +
                       $"-Partition {partition} " +
                       $"-FsType {fsType}";

        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = arguments,
            Verb = "runas", // Request elevation
            UseShellExecute = true, // Required for elevation
            CreateNoWindow = false,
            WindowStyle = ProcessWindowStyle.Hidden
        };

        try
        {
            using var process = Process.Start(startInfo);
            if (process == null)
            {
                return new MountResult
                {
                    Success = false,
                    ErrorMessage = "Failed to start PowerShell process."
                };
            }

            await process.WaitForExitAsync();

            // Note: When using runas with UseShellExecute = true, we cannot capture stdout directly.
            // We need to modify our approach: write output to a temp file from the script,
            // or use a different elevation method.
            // For MVP, we'll use a temp file approach.

            // Read output from temp file (script should write to a known location)
            var tempOutputFile = Path.Combine(Path.GetTempPath(), $"limount_mount_{diskIndex}_{partition}.txt");

            // Wait a moment for file to be written
            await Task.Delay(500);

            if (File.Exists(tempOutputFile))
            {
                var output = await File.ReadAllTextAsync(tempOutputFile);
                File.Delete(tempOutputFile);

                var parsedValues = KeyValueOutputParser.Parse(output);
                return MountResult.FromDictionary(parsedValues);
            }

            // Fallback: assume success if exit code is 0
            if (process.ExitCode == 0)
            {
                return new MountResult
                {
                    Success = true,
                    ErrorMessage = "Mount script completed but output file not found. Assuming success."
                };
            }

            return new MountResult
            {
                Success = false,
                ErrorMessage = $"Mount script exited with code {process.ExitCode}"
            };
        }
        catch (Exception ex)
        {
            return new MountResult
            {
                Success = false,
                ErrorMessage = $"Failed to execute mount script: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Executes the Map-WSLShareToDrive.ps1 script (non-elevated).
    /// </summary>
    private async Task<MappingResult> ExecuteMappingScriptAsync(char driveLetter, string targetUNC)
    {
        var scriptPath = GetScriptPath("Map-WSLShareToDrive.ps1");

        if (!File.Exists(scriptPath))
        {
            return new MappingResult
            {
                Success = false,
                ErrorMessage = $"Mapping script not found at: {scriptPath}"
            };
        }

        var arguments = $"-ExecutionPolicy Bypass -File \"{scriptPath}\" " +
                       $"-DriveLetter {driveLetter} " +
                       $"-TargetUNC \"{targetUNC}\"";

        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        try
        {
            using var process = Process.Start(startInfo);
            if (process == null)
            {
                return new MappingResult
                {
                    Success = false,
                    ErrorMessage = "Failed to start PowerShell process."
                };
            }

            var output = await process.StandardOutput.ReadToEndAsync();
            var errorOutput = await process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync();

            var parsedValues = KeyValueOutputParser.Parse(output);
            var result = MappingResult.FromDictionary(parsedValues);

            // If parsing failed but exit code is 0, assume success
            if (process.ExitCode == 0 && !result.Success && string.IsNullOrEmpty(result.ErrorMessage))
            {
                result.Success = true;
                result.DriveLetter = driveLetter.ToString();
                result.TargetUNC = targetUNC;
            }

            if (!result.Success && !string.IsNullOrEmpty(errorOutput))
            {
                result.ErrorMessage = $"{result.ErrorMessage}\n{errorOutput}";
            }

            return result;
        }
        catch (Exception ex)
        {
            return new MappingResult
            {
                Success = false,
                ErrorMessage = $"Failed to execute mapping script: {ex.Message}"
            };
        }
    }

    private string GetScriptPath(string scriptFileName)
    {
        // Try to locate the script relative to the application directory
        var appDirectory = AppDomain.CurrentDomain.BaseDirectory;

        // Look for scripts folder in several locations:
        // 1. <AppDirectory>/scripts
        // 2. <AppDirectory>/../../../scripts (for development builds)
        // 3. <AppDirectory>/../../../../scripts (for deeper nested builds)

        var possiblePaths = new[]
        {
            Path.Combine(appDirectory, "scripts", scriptFileName),
            Path.Combine(appDirectory, "..", "..", "..", "scripts", scriptFileName),
            Path.Combine(appDirectory, "..", "..", "..", "..", "scripts", scriptFileName),
            Path.Combine(appDirectory, "..", "..", "..", "..", "..", "scripts", scriptFileName)
        };

        foreach (var path in possiblePaths)
        {
            var fullPath = Path.GetFullPath(path);
            if (File.Exists(fullPath))
            {
                return fullPath;
            }
        }

        // Fallback: assume scripts are in the repo root
        return Path.Combine(appDirectory, "scripts", scriptFileName);
    }
}
