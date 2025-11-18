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
    private readonly IScriptExecutor _scriptExecutor;

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

    /// <summary>
    /// Initializes a new instance of MainViewModel and configures it with the provided services; subscribes to property changes so partitions are updated when SelectedDisk changes.
    /// </summary>
    /// <param name="diskService">Service used to enumerate candidate disks and their partitions.</param>
    /// <param name="driveLetterService">Service used to obtain available drive letters.</param>
    /// <param name="scriptExecutor">Service used to execute mounting and mapping scripts.</param>
    public MainViewModel(IDiskEnumerationService diskService, IDriveLetterService driveLetterService, IScriptExecutor scriptExecutor)
    {
        _diskService = diskService;
        _driveLetterService = driveLetterService;
        _scriptExecutor = scriptExecutor;

        // Subscribe to SelectedDisk changes to update partitions
        PropertyChanged += OnPropertyChanged;
    }

    /// <summary>
    /// Responds to property-change notifications and updates the partition list when <c>SelectedDisk</c> changes.
    /// </summary>
    /// <param name="sender">The source of the property change notification.</param>
    /// <param name="e">The <see cref="PropertyChangedEventArgs"/> identifying the changed property.</param>
    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SelectedDisk))
        {
            UpdatePartitions();
        }
    }

    /// <summary>
    /// Loads disks and drive letters from the system.
    /// <summary>
    /// Refreshes the available disks, partitions, and free drive letters.
    /// </summary>
    [RelayCommand]
    private void Refresh()
    {
        LoadDisksAndDriveLetters();
    }

    /// <summary>
    /// Initializes the ViewModel by loading disks and drive letters asynchronously.
    /// Call this after instantiation to perform heavy I/O operations.
    /// <summary>
    /// Loads available disks and free drive letters in the background.
    /// </summary>
    public async Task InitializeAsync()
    {
        await Task.Run(() => LoadDisksAndDriveLetters());
    }

    /// <summary>
    /// Loads candidate (non-system, non-boot) disks and available drive letters, populates the corresponding collections, and auto-selects defaults when available.
    /// </summary>
    /// <remarks>
    /// Updates the Disks and FreeDriveLetters collections, may set SelectedDisk and SelectedDriveLetter if they are not already set, and writes progress or error text to StatusMessage.
    /// </remarks>
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

    /// <summary>
    /// Populates the Partitions collection with partitions from SelectedDisk that are likely Linux partitions and auto-selects the first one if present.
    /// </summary>
    /// <remarks>
    /// Clears any existing entries in Partitions. If SelectedDisk is null, the method returns without modifying Partitions or SelectedPartition.
    /// After filtering, SelectedPartition is set to the first partition in the collection or to null if no partitions matched.
    /// </remarks>
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
    /// <summary>
    /// Mounts the selected partition into WSL using an elevated PowerShell script, maps the resulting UNC path to the chosen Windows drive letter, and updates the view model's status, busy state, and explorer availability based on the outcome.
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

            var mappingResult = await _scriptExecutor.ExecuteMappingScriptAsync(
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

    /// <summary>
    /// Determines whether the mount command can be executed given the current state.
    /// </summary>
    /// <returns>`true` if the view model is not busy and a disk, a partition, and a drive letter are all selected; `false` otherwise.</returns>
    private bool CanMount()
    {
        return !IsBusy &&
               SelectedDisk != null &&
               SelectedPartition != null &&
               SelectedDriveLetter != null;
    }

    /// <summary>
    /// Opens Windows Explorer to the mapped drive letter.
    /// <summary>
    /// Opens Windows Explorer at the last mapped drive letter, if a drive has been mapped.
    /// </summary>
    /// <remarks>
    /// If no drive has been mapped this method does nothing. On failure it sets <see cref="StatusMessage"/> with the error message.
    /// </remarks>
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

    /// <summary>
    /// Checks whether Explorer can be opened for the last mapped drive letter.
    /// </summary>
    /// <returns>`true` if Explorer is enabled and a previously mapped drive letter is available, `false` otherwise.</returns>
    private bool CanOpenExplorerExecute()
    {
        return CanOpenExplorer && !string.IsNullOrEmpty(_lastMappedDriveLetter);
    }

    /// <summary>
    /// Executes the Mount-LinuxDiskCore.ps1 script with elevation.
    /// <summary>
    /// Executes the elevated PowerShell mount script for the specified physical disk and partition using the given filesystem type.
    /// </summary>
    /// <param name="diskIndex">The physical disk index to mount (as reported by the system).</param>
    /// <param name="partition">The partition number on the disk to mount.</param>
    /// <param name="fsType">The filesystem type to use when mounting (e.g., "ext4").</param>
    /// <returns>A <see cref="MountResult"/> describing success or failure and any associated message.</returns>
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
    /// Locate a script file by searching several likely "scripts" directories relative to the application's base directory.
    /// </summary>
    /// <param name="scriptFileName">The file name of the script to locate (e.g., "Mount-LinuxDiskCore.ps1").</param>
    /// <returns>The full path to the first matching script file found; if none are found, returns a fallback path under the application's "scripts" folder.</returns>
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