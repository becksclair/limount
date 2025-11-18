using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
    private readonly DiskEnumerationService _diskService;
    private readonly DriveLetterService _driveLetterService;

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
    /// Initializes a new MainViewModel, prepares disk and drive-letter services, subscribes to selection changes, and loads initial disk and drive letter data.
    /// </summary>
    public MainViewModel()
    {
        _diskService = new DiskEnumerationService();
        _driveLetterService = new DriveLetterService();

        // Subscribe to SelectedDisk changes to update partitions
        PropertyChanged += OnPropertyChanged;

        // Initial load
        LoadDisksAndDriveLetters();
    }

    /// <summary>
    /// Handles property change notifications and refreshes the partition list when the SelectedDisk property changes.
    /// </summary>
    /// <param name="sender">The source of the property change event (typically this view model).</param>
    /// <param name="e">Property change details; when <see cref="PropertyChangedEventArgs.PropertyName"/> equals <c>SelectedDisk</c>, <see cref="UpdatePartitions"/> is invoked.</param>
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
    /// Reloads the list of candidate disks and available drive letters and refreshes related view state.
    /// </summary>
    [RelayCommand]
    private void Refresh()
    {
        LoadDisksAndDriveLetters();
    }

    /// <summary>
    /// Loads candidate (non-system) disks and available drive letters into the view model's collections.
    /// </summary>
    /// <remarks>
    /// Populates the Disks and FreeDriveLetters collections from the disk and drive-letter services,
    /// auto-selects the first disk and first free drive letter if none are selected, and updates StatusMessage
    /// to reflect progress and results. On error, StatusMessage is set to the error message.
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
    /// Refreshes the Partitions collection to contain only partitions on the currently selected disk that are likely Linux, and selects the first one if present.
    /// </summary>
    /// <remarks>
    /// Clears the existing Partitions collection before repopulating. If no disk is selected, the method returns without modifying SelectedPartition.
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
    /// Coordinates mounting the selected disk partition into WSL and mapping the resulting UNC share to the chosen Windows drive letter.
    /// </summary>
    /// <remarks>
    /// Validates that a disk, partition, and drive letter are selected; sets IsBusy while the operation runs; invokes the mount step and then the mapping step; updates StatusMessage and CanOpenExplorer based on outcome; and records the last mapped drive letter on success.
    /// </remarks>
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

    /// <summary>
    /// Determines whether the mount command is currently allowed to run.
    /// </summary>
    /// <returns>`true` if the view model is not busy and a disk, partition, and drive letter are selected; `false` otherwise.</returns>
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
    /// Opens Windows Explorer at the last mapped drive letter.
    /// </summary>
    /// <remarks>
    /// If no drive letter has been mapped this method does nothing. On failure the <see cref="StatusMessage"/> property is updated with the error message.
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
    /// Determines whether the OpenExplorer command can execute based on the view model's state.
    /// </summary>
    /// <returns>`true` if Explorer can be opened and a mapped drive letter is available, `false` otherwise.</returns>
    private bool CanOpenExplorerExecute()
    {
        return CanOpenExplorer && !string.IsNullOrEmpty(_lastMappedDriveLetter);
    }

    /// <summary>
    /// Executes the Mount-LinuxDiskCore.ps1 script with elevation.
    /// <summary>
    /// Executes the elevated PowerShell mount script to mount the specified disk partition into WSL and returns the parsed result.
    /// </summary>
    /// <param name="diskIndex">Physical disk index to mount.</param>
    /// <param name="partition">Partition number on the disk to mount.</param>
    /// <param name="fsType">Filesystem type to use when mounting (for example, "ext4").</param>
    /// <returns>MountResult containing the operation outcome, mount path information when successful, or an error message when failed.</returns>
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
    /// <summary>
    /// Runs the Map-WSLShareToDrive.ps1 PowerShell script to map a WSL share to a Windows drive letter.
    /// </summary>
    /// <param name="driveLetter">The drive letter to assign (single character, e.g., 'X').</param>
    /// <param name="targetUNC">The UNC path of the WSL share to map (e.g., \\wsl$\distro\path).</param>
    /// <returns>A <see cref="MappingResult"/> describing success, the assigned drive letter and target UNC on success, and any error message on failure.</returns>
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

    /// <summary>
    /// Resolves the filesystem path to a PowerShell script by searching several candidate locations relative to the application's base directory.
    /// </summary>
    /// <param name="scriptFileName">The script file name (for example "Mount-LinuxDiskCore.ps1").</param>
    /// <returns>The full path to the first matching script found; if none are found, returns the fallback path under the application's "scripts" subdirectory.</returns>
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