using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiMount.App.Services;
using LiMount.App.Views;
using LiMount.Core.Interfaces;
using LiMount.Core.Configuration;
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
    private readonly IMountOrchestrator _mountOrchestrator;
    private readonly IUnmountOrchestrator _unmountOrchestrator;
    private readonly IMountStateService _mountStateService;
    private readonly IEnvironmentValidationService _environmentValidationService;
    private readonly IScriptExecutor _scriptExecutor;
    private readonly IDialogService _dialogService;
    private readonly Func<Views.HistoryWindow> _historyWindowFactory;
    private readonly ILogger<MainViewModel> _logger;
    private readonly LiMountConfiguration _config;

    private string? _detectedFsType;

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

    /// <summary>
    /// Detected filesystem type from the selected partition.
    /// </summary>
    public string DetectedFileSystem => GetDetectedFileSystem();

    /// <summary>
    /// Whether filesystem detection is in progress.
    /// </summary>
    [ObservableProperty]
    private bool _isDetectingFs;

    /// <summary>
    /// Gets the filesystem type to use for mounting.
    /// </summary>
    private string GetFileSystemTypeForMount()
    {
        if (!string.IsNullOrEmpty(_detectedFsType))
            return _detectedFsType;
        return "auto";
    }

    /// <summary>
    /// Gets the display string for the detected filesystem.
    /// </summary>
    private string GetDetectedFileSystem()
    {
        if (SelectedPartition == null)
            return "Select a partition";
        if (IsDetectingFs)
            return "Detecting...";
        if (!string.IsNullOrEmpty(_detectedFsType))
            return _detectedFsType.ToUpperInvariant();
        return "Click Detect to identify";
    }

    [ObservableProperty]
    private string _statusMessage = "Ready. Select a disk, partition, and drive letter, then click Mount.";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _canOpenExplorer;

    /// <summary>
    /// The disk index of the currently mounted disk, or null if nothing is mounted.
    /// </summary>
    [ObservableProperty]
    private int? _currentMountedDiskIndex;

    /// <summary>
    /// The partition number of the currently mounted partition, or null if nothing is mounted.
    /// </summary>
    [ObservableProperty]
    private int? _currentMountedPartition;

    /// <summary>
    /// The drive letter of the currently mounted partition, or null if nothing is mounted.
    /// </summary>
    [ObservableProperty]
    private char? _currentMountedDriveLetter;

    /// <summary>
    /// Gets whether a disk is currently mounted.
    /// </summary>
    public bool IsMounted => CurrentMountedDiskIndex.HasValue;

    /// <summary>
    /// Called when CurrentMountedDiskIndex changes to notify IsMounted property.
    /// </summary>
    partial void OnCurrentMountedDiskIndexChanged(int? value)
    {
        OnPropertyChanged(nameof(IsMounted));
    }

    /// <summary>
    /// Initializes a new instance of MainViewModel and configures it with the provided services; subscribes to property changes so partitions are updated when SelectedDisk changes.
    /// </summary>
    /// <param name="diskService">Service used to enumerate candidate disks and their partitions.</param>
    /// <param name="driveLetterService">Service used to obtain available drive letters.</param>
    /// <param name="mountOrchestrator">Service used to orchestrate mounting and mapping operations.</param>
    /// <param name="unmountOrchestrator">Service used to orchestrate unmounting and unmapping operations.</param>
    /// <param name="mountStateService">Service used to track active mount state persistently.</param>
    /// <param name="environmentValidationService">Service used to validate the environment meets requirements.</param>
    /// <param name="scriptExecutor">Service used to execute scripts and detect filesystems.</param>
    /// <param name="dialogService">Service used to display dialogs to the user.</param>
    /// <param name="historyWindowFactory">Factory for creating history window instances.</param>
    /// <param name="logger">Logger for diagnostic information.</param>
    /// <param name="config">Configuration options for LiMount.</param>
    public MainViewModel(
        IDiskEnumerationService diskService,
        IDriveLetterService driveLetterService,
        IMountOrchestrator mountOrchestrator,
        IUnmountOrchestrator unmountOrchestrator,
        IMountStateService mountStateService,
        IEnvironmentValidationService environmentValidationService,
        IScriptExecutor scriptExecutor,
        IDialogService dialogService,
        Func<Views.HistoryWindow> historyWindowFactory,
        ILogger<MainViewModel> logger,
        IOptions<LiMountConfiguration> config)
    {
        _diskService = diskService;
        _driveLetterService = driveLetterService;
        _mountOrchestrator = mountOrchestrator;
        _unmountOrchestrator = unmountOrchestrator;
        _mountStateService = mountStateService;
        _environmentValidationService = environmentValidationService;
        _scriptExecutor = scriptExecutor;
        _dialogService = dialogService;
        _historyWindowFactory = historyWindowFactory;
        _logger = logger;
        _config = config.Value;

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
            _detectedFsType = null;
            OnPropertyChanged(nameof(DetectedFileSystem));
            MountCommand.NotifyCanExecuteChanged();
        }
        else if (e.PropertyName == nameof(SelectedPartition))
        {
            // Clear detection and auto-detect when partition changes
            _detectedFsType = null;
            OnPropertyChanged(nameof(DetectedFileSystem));
            MountCommand.NotifyCanExecuteChanged();

            // Auto-detect filesystem type
            if (SelectedDisk != null && SelectedPartition != null)
            {
                _ = DetectFilesystemAsync();
            }
        }
        else if (e.PropertyName == nameof(SelectedDriveLetter))
        {
            MountCommand.NotifyCanExecuteChanged();
        }
    }

    /// <summary>
    /// Loads disks and drive letters from the system.
    /// Refreshes the available disks, partitions, and free drive letters.
    /// </summary>
    [RelayCommand]
    private async Task RefreshAsync()
    {
        await LoadDisksAndDriveLetters();
    }

    /// <summary>
    /// Detects the filesystem type of the selected partition using WSL.
    /// Requires elevation to temporarily attach the disk.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanDetectFilesystem))]
    private async Task DetectFilesystemAsync()
    {
        if (SelectedDisk == null || SelectedPartition == null)
            return;

        IsDetectingFs = true;
        OnPropertyChanged(nameof(DetectedFileSystem));
        StatusMessage = "Detecting filesystem type (UAC prompt required)...";

        try
        {
            var fsType = await _scriptExecutor.DetectFilesystemTypeAsync(
                SelectedDisk.Index,
                SelectedPartition.PartitionNumber);

            _detectedFsType = fsType;
            OnPropertyChanged(nameof(DetectedFileSystem));

            if (!string.IsNullOrEmpty(fsType))
            {
                StatusMessage = $"Detected filesystem: {fsType.ToUpperInvariant()}";
            }
            else
            {
                StatusMessage = "Could not detect filesystem type. Will use auto-detection during mount.";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to detect filesystem type");
            StatusMessage = $"Detection failed: {ex.Message}";
        }
        finally
        {
            IsDetectingFs = false;
            OnPropertyChanged(nameof(DetectedFileSystem));
        }
    }

    /// <summary>
    /// Determines if filesystem detection can be executed.
    /// </summary>
    private bool CanDetectFilesystem() => SelectedDisk != null && SelectedPartition != null && !IsBusy && !IsDetectingFs;

    /// <summary>
    /// Initializes the ViewModel by validating the environment and loading disks and drive letters asynchronously.
    /// Call this after instantiation to perform heavy I/O operations.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel initialization if the window closes.</param>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        StatusMessage = "Validating environment...";
        _logger.LogInformation("Starting application initialization with environment validation");

        // Validate environment first
        var validationResult = await _environmentValidationService.ValidateEnvironmentAsync();

        if (!validationResult.IsValid)
        {
            _logger.LogWarning("Environment validation failed. Errors: {Errors}", string.Join("; ", validationResult.Errors));

            // Build detailed error message with suggestions
            var errorMessage = "LiMount cannot start because your system does not meet the requirements:\n\n";
            errorMessage += string.Join("\n", validationResult.Errors.Select(e => $"• {e}"));
            errorMessage += "\n\nTo fix these issues:\n\n";
            errorMessage += string.Join("\n", validationResult.Suggestions.Select(s => $"  {s}"));

            await _dialogService.ShowErrorAsync(errorMessage, "Environment Validation Failed");

            StatusMessage = "Environment validation failed. Please check the requirements.";
            return;
        }

        _logger.LogInformation("Environment validation successful. WSL distros: {Distros}",
            string.Join(", ", validationResult.InstalledDistros));

        // Reconcile mount state on startup if configured
        if (_config.Initialization.AutoReconcileMounts)
        {
            StatusMessage = "Reconciling mount state...";
            try
            {
                var orphanedMounts = await _mountStateService.ReconcileMountStateAsync();
                if (orphanedMounts.Count > 0)
                {
                    _logger.LogInformation("Reconciliation found {Count} orphaned mount(s)", orphanedMounts.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Mount state reconciliation failed, continuing with initialization");
            }
        }

        StatusMessage = $"Environment OK. Found {validationResult.InstalledDistros.Count} WSL distro(s). Loading disks...";

        // Run data retrieval on background thread
        var data = await Task.Run(GetDisksAndDriveLettersData, cancellationToken);

        // Update UI on UI thread
        await Application.Current.Dispatcher.InvokeAsync(() => UpdateUIWithData(data));
        
        // Check for existing mounts
        await DetectExistingMountsAsync();
    }
    
    /// <summary>
    /// Detects any existing WSL mounts and drive letter mappings from previous sessions.
    /// Updates the mount state if a valid mount is found.
    /// </summary>
    private async Task DetectExistingMountsAsync()
    {
        try
        {
            StatusMessage = "Checking for existing mounts...";
            
            // First check our persisted mount state
            var activeMounts = await _mountStateService.GetActiveMountsAsync();
            if (activeMounts.Count > 0)
            {
                var mount = activeMounts.First();
                
                // Verify the mount is still valid by checking if the UNC path exists
                var uncPathExists = !string.IsNullOrEmpty(mount.MountPathUNC) && 
                                    Directory.Exists(mount.MountPathUNC);
                
                if (uncPathExists)
                {
                    CurrentMountedDiskIndex = mount.DiskIndex;
                    CurrentMountedPartition = mount.PartitionNumber;
                    CurrentMountedDriveLetter = mount.DriveLetter;
                    CanOpenExplorer = true;
                    
                    StatusMessage = $"Found existing mount: Disk {mount.DiskIndex} partition {mount.PartitionNumber} → {mount.DriveLetter}:";
                    _logger.LogInformation("Detected existing mount from state: Disk {DiskIndex} partition {Partition} at {DriveLetter}:",
                        mount.DiskIndex, mount.PartitionNumber, mount.DriveLetter);
                    
                    UnmountCommand.NotifyCanExecuteChanged();
                    OpenExplorerCommand.NotifyCanExecuteChanged();
                    return;
                }
                else
                {
                    // Mount state exists but is stale - clean it up
                    _logger.LogInformation("Stale mount state found for disk {DiskIndex}, cleaning up", mount.DiskIndex);
                    await _mountStateService.UnregisterMountAsync(mount.DiskIndex);
                }
            }
            
            // If no persisted state, check for WSL mounts + subst mappings directly
            var detectedMount = await DetectMountFromSystemAsync();
            if (detectedMount != null)
            {
                CurrentMountedDiskIndex = detectedMount.Value.diskIndex;
                CurrentMountedPartition = detectedMount.Value.partition;
                
                // Check if there's a drive letter assigned ('\0' means WSL mount without drive mapping)
                if (detectedMount.Value.driveLetter != '\0')
                {
                    CurrentMountedDriveLetter = detectedMount.Value.driveLetter;
                    CanOpenExplorer = true;
                    StatusMessage = $"Detected existing mount: Disk {detectedMount.Value.diskIndex} → {detectedMount.Value.driveLetter}:";
                }
                else
                {
                    // WSL mount exists but no drive letter - need to unmount to remount properly
                    CurrentMountedDriveLetter = null;
                    CanOpenExplorer = false;
                    StatusMessage = $"Detected WSL mount for Disk {detectedMount.Value.diskIndex} (no drive letter). Click Unmount to clean up.";
                }
                
                _logger.LogInformation("Detected existing mount from system: Disk {DiskIndex} partition {Partition}, drive letter: {DriveLetter}",
                    detectedMount.Value.diskIndex, detectedMount.Value.partition, 
                    detectedMount.Value.driveLetter == '\0' ? "(none)" : detectedMount.Value.driveLetter.ToString());
                
                UnmountCommand.NotifyCanExecuteChanged();
                OpenExplorerCommand.NotifyCanExecuteChanged();
                MountCommand.NotifyCanExecuteChanged();
            }
            else
            {
                StatusMessage = $"Found {Disks.Count} candidate disk(s). Ready to mount.";
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to detect existing mounts");
            StatusMessage = $"Found {Disks.Count} candidate disk(s). Ready to mount.";
        }
    }
    
    /// <summary>
    /// Checks the system for existing WSL mounts and subst mappings.
    /// </summary>
    private async Task<(int diskIndex, int partition, char driveLetter)?> DetectMountFromSystemAsync()
    {
        return await Task.Run<(int diskIndex, int partition, char driveLetter)?>(() =>
        {
            // First check subst mappings
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "subst",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                
                using var process = Process.Start(psi);
                if (process != null)
                {
                    var output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit(5000);
                    
                    // Parse subst output: "Z:\: => UNC\wsl.localhost\Ubuntu-24.04\mnt\wsl\PHYSICALDRIVE1p1"
                    foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                    {
                        var match = Regex.Match(
                            line, 
                            @"^([A-Z]):\\: => .+PHYSICALDRIVE(\d+)p(\d+)",
                            RegexOptions.IgnoreCase);
                        
                        if (match.Success)
                        {
                            var driveLetter = match.Groups[1].Value[0];
                            var diskIndex = int.Parse(match.Groups[2].Value);
                            var partition = int.Parse(match.Groups[3].Value);
                            
                            _logger.LogDebug("Found subst mapping: {DriveLetter}: -> Disk {DiskIndex} partition {Partition}",
                                driveLetter, diskIndex, partition);
                            
                            return (diskIndex, partition, driveLetter);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to check subst mappings");
            }
            
            // Also check for WSL-only mounts (no drive letter mapping)
            try
            {
                // Check /mnt/wsl for mounted drives
                var wslPsi = new ProcessStartInfo
                {
                    FileName = "wsl.exe",
                    Arguments = "-e ls /mnt/wsl",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                
                using var wslProcess = Process.Start(wslPsi);
                if (wslProcess != null)
                {
                    var wslOutput = wslProcess.StandardOutput.ReadToEnd();
                    wslProcess.WaitForExit(5000);
                    
                    foreach (var entry in wslOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                    {
                        var match = Regex.Match(entry.Trim(), @"PHYSICALDRIVE(\d+)p(\d+)", RegexOptions.IgnoreCase);
                        if (match.Success)
                        {
                            var diskIndex = int.Parse(match.Groups[1].Value);
                            var partition = int.Parse(match.Groups[2].Value);
                            
                            _logger.LogDebug("Found WSL mount without drive letter: Disk {DiskIndex} partition {Partition}",
                                diskIndex, partition);
                            
                            // Return with null-char to indicate no drive letter assigned
                            return (diskIndex, partition, '\0');
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to check WSL mounts");
            }
            
            return null;
        });
    }

    /// <summary>
    /// Loads candidate (non-system, non-boot) disks and available drive letters, populates the corresponding collections, and auto-selects defaults when available.
    /// </summary>
    /// <remarks>
    /// Updates the Disks and FreeDriveLetters collections, may set SelectedDisk and SelectedDriveLetter if they are not already set, and writes progress or error text to StatusMessage.
    /// </remarks>
    private async Task LoadDisksAndDriveLetters()
    {
        try
        {
            StatusMessage = "Loading disks and drive letters...";

            // Get data on background thread
            var data = await Task.Run(GetDisksAndDriveLettersData);

            // Update UI on UI thread
            UpdateUIWithData(data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load disks and drive letters");
            StatusMessage = $"Error loading disks: {ex.Message}";
        }
    }

    /// <summary>
    /// Retrieves disks and drive letters data without touching UI components.
    /// This method is safe to call from any thread.
    /// </summary>
    /// <returns>Tuple containing candidate disks and free drive letters</returns>
    private (IEnumerable<DiskInfo> candidateDisks, IEnumerable<DriveLetterInfo> freeLetters) GetDisksAndDriveLettersData()
    {
        // Get candidate disks (non-system, non-boot)
        var candidateDisks = _diskService.GetCandidateDisks();

        // Get free drive letters (sorted Z→A) and convert to DriveLetterInfo objects
        var freeLetters = _driveLetterService.GetFreeLetters()
            .Select(letter => new DriveLetterInfo { Letter = letter, IsInUse = false });

        return (candidateDisks, freeLetters);
    }

    /// <summary>
    /// Updates UI collections and properties with the provided data.
    /// This method must be called on the UI thread.
    /// </summary>
    private void UpdateUIWithData((IEnumerable<DiskInfo> candidateDisks, IEnumerable<DriveLetterInfo> freeLetters) data)
    {
        var (candidateDisks, freeLetters) = data;

        Disks.Clear();
        foreach (var disk in candidateDisks)
        {
            Disks.Add(disk);
        }

        FreeDriveLetters.Clear();
        foreach (var letter in freeLetters)
        {
            FreeDriveLetters.Add(letter.Letter);
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
            var progress = new Progress<string>(msg => StatusMessage = msg);

            var result = await _mountOrchestrator.MountAndMapAsync(
                SelectedDisk.Index,
                SelectedPartition.PartitionNumber,
                SelectedDriveLetter.Value,
                GetFileSystemTypeForMount(),
                null,
                progress);

            if (!result.Success)
            {
                StatusMessage = result.ErrorMessage ?? "Mount and map operation failed.";
                return;
            }

            // Success! Track the mount state
            CurrentMountedDiskIndex = SelectedDisk.Index;
            CurrentMountedPartition = SelectedPartition.PartitionNumber;
            var driveLetter = result.DriveLetter ?? SelectedDriveLetter.Value;
            CurrentMountedDriveLetter = driveLetter;
            CanOpenExplorer = true;
            
            // Notify commands that mount state changed
            UnmountCommand.NotifyCanExecuteChanged();
            OpenExplorerCommand.NotifyCanExecuteChanged();
            MountCommand.NotifyCanExecuteChanged();

            // Register mount state persistently
            var activeMount = new ActiveMount
            {
                Id = Guid.NewGuid().ToString(),
                MountedAt = DateTime.Now,
                DiskIndex = result.DiskIndex,
                PartitionNumber = result.Partition,
                DriveLetter = driveLetter,
                DistroName = result.DistroName ?? string.Empty,
                MountPathLinux = result.MountPathLinux ?? string.Empty,
                MountPathUNC = result.MountPathUNC ?? string.Empty,
                IsVerified = true,
                LastVerified = DateTime.Now
            };
            
            try
            {
                await _mountStateService.RegisterMountAsync(activeMount);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to persist mount state for disk {DiskIndex} partition {Partition}", result.DiskIndex, result.Partition);
                StatusMessage = $"Success! Mounted as {SelectedDriveLetter}: - Warning: Could not save mount state to history.";
            }

            StatusMessage = $"Success! Mounted as {SelectedDriveLetter}: - You can now access the Linux partition from Windows Explorer.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Mount operation failed for disk {DiskIndex} partition {Partition}", SelectedDisk?.Index, SelectedPartition?.PartitionNumber);
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
               !IsMounted &&
               SelectedDisk != null &&
               SelectedPartition != null &&
               SelectedDriveLetter != null;
    }

    /// <summary>
    /// Opens Windows Explorer to the mapped drive letter.
    /// Opens Windows Explorer at the last mapped drive letter, if a drive has been mapped.
    /// </summary>
    /// <remarks>
    /// If no drive has been mapped this method does nothing. On failure it sets <see cref="StatusMessage"/> with the error message.
    /// </remarks>
    [RelayCommand(CanExecute = nameof(CanOpenExplorerExecute))]
    private void OpenExplorer()
    {
        if (!CurrentMountedDriveLetter.HasValue)
        {
            return;
        }

        try
        {
            var drivePath = $"{CurrentMountedDriveLetter.Value}:\\";
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
        return CanOpenExplorer && CurrentMountedDriveLetter.HasValue;
    }

    /// <summary>
    /// Unmounts the currently mounted disk from WSL2 and unmaps the Windows drive letter.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanUnmount))]
    private async Task UnmountAsync()
    {
        if (!CurrentMountedDiskIndex.HasValue)
        {
            StatusMessage = "No disk is currently mounted.";
            return;
        }

        // Ask for confirmation
        var confirmed = await _dialogService.ConfirmAsync(
            $"Are you sure you want to unmount disk {CurrentMountedDiskIndex} (Drive {CurrentMountedDriveLetter?.ToString() ?? "-"}:)?\n\n" +
            "Make sure you have saved and closed any files on this drive before unmounting.",
            "Confirm Unmount",
            DialogType.Warning);

        if (!confirmed)
        {
            StatusMessage = "Unmount cancelled.";
            return;
        }

        IsBusy = true;

        try
        {
            var progress = new Progress<string>(msg => StatusMessage = msg);

            var unmountResult = await _unmountOrchestrator.UnmountAndUnmapAsync(
                CurrentMountedDiskIndex.Value,
                CurrentMountedDriveLetter,
                progress);

            if (!unmountResult.Success)
            {
                StatusMessage = unmountResult.ErrorMessage ?? "Unmount operation failed.";
                return;
            }

            // Success! Clear mount state
            var diskIndex = CurrentMountedDiskIndex.Value;
            
            try
            {
                await _mountStateService.UnregisterMountAsync(diskIndex);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to unregister mount state for disk {DiskIndex}, but proceeding with UI cleanup", diskIndex);
            }

            StatusMessage = $"Successfully unmounted disk {diskIndex}.";
            CurrentMountedDiskIndex = null;
            CurrentMountedPartition = null;
            CurrentMountedDriveLetter = null;
            CanOpenExplorer = false;
            MountCommand.NotifyCanExecuteChanged();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unmount operation failed for disk {DiskIndex}", CurrentMountedDiskIndex);
            StatusMessage = $"Error during unmount: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Determines whether the unmount command can be executed.
    /// </summary>
    /// <returns>`true` if not busy and a disk is currently mounted, `false` otherwise.</returns>
    private bool CanUnmount()
    {
        return !IsBusy && IsMounted;
    }

    /// <summary>
    /// Opens the mount history window to display past operations.
    /// </summary>
    [RelayCommand]
    private void OpenHistory()
    {
        try
        {
            var historyWindow = _historyWindowFactory();
            historyWindow.Owner = Application.Current.MainWindow;
            historyWindow.ShowDialog();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open history window");
            _ = _dialogService.ShowErrorAsync($"Failed to open history window:\n\n{ex.Message}", "Error");
        }
    }
}