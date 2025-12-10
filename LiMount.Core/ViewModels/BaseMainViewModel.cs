using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using LiMount.Core.Abstractions;
using LiMount.Core.Configuration;
using LiMount.Core.Interfaces;
using LiMount.Core.Models;

namespace LiMount.Core.ViewModels;

/// <summary>
/// Base ViewModel for the LiMount main window.
/// Contains all shared logic between WPF and WinUI implementations.
/// Platform-specific behavior is handled via abstract methods.
/// </summary>
public abstract partial class BaseMainViewModel : ObservableObject
{
    /// <summary>
    /// Regex timeout in milliseconds to prevent ReDoS attacks on untrusted input.
    /// </summary>
    protected const int RegexTimeoutMs = 100;

    /// <summary>
    /// Maximum valid disk index to prevent integer overflow attacks.
    /// </summary>
    protected const int MaxDiskIndex = 99;

    /// <summary>
    /// Maximum valid partition number to prevent integer overflow attacks.
    /// </summary>
    protected const int MaxPartition = 99;

    protected readonly IDiskEnumerationService DiskService;
    protected readonly IDriveLetterService DriveLetterService;
    protected readonly IMountOrchestrator MountOrchestrator;
    protected readonly IUnmountOrchestrator UnmountOrchestrator;
    protected readonly IMountStateService MountStateService;
    protected readonly IEnvironmentValidationService EnvironmentValidationService;
    protected readonly IFilesystemDetectionService FilesystemDetectionService;
    protected readonly IDialogService DialogService;
    protected readonly ILogger Logger;
    protected readonly LiMountConfiguration Config;

    private string? _detectedFsType;

    #region Observable Properties

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

    #endregion

    /// <summary>
    /// Initializes a new instance of <see cref="BaseMainViewModel"/> with required services.
    /// </summary>
    protected BaseMainViewModel(
        IDiskEnumerationService diskService,
        IDriveLetterService driveLetterService,
        IMountOrchestrator mountOrchestrator,
        IUnmountOrchestrator unmountOrchestrator,
        IMountStateService mountStateService,
        IEnvironmentValidationService environmentValidationService,
        IFilesystemDetectionService filesystemDetectionService,
        IDialogService dialogService,
        ILogger logger,
        LiMountConfiguration config)
    {
        DiskService = diskService ?? throw new ArgumentNullException(nameof(diskService));
        DriveLetterService = driveLetterService ?? throw new ArgumentNullException(nameof(driveLetterService));
        MountOrchestrator = mountOrchestrator ?? throw new ArgumentNullException(nameof(mountOrchestrator));
        UnmountOrchestrator = unmountOrchestrator ?? throw new ArgumentNullException(nameof(unmountOrchestrator));
        MountStateService = mountStateService ?? throw new ArgumentNullException(nameof(mountStateService));
        EnvironmentValidationService = environmentValidationService ?? throw new ArgumentNullException(nameof(environmentValidationService));
        FilesystemDetectionService = filesystemDetectionService ?? throw new ArgumentNullException(nameof(filesystemDetectionService));
        DialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        Config = config ?? throw new ArgumentNullException(nameof(config));

        PropertyChanged += OnPropertyChangedHandler;
    }

    #region Abstract Methods for Platform-Specific Behavior

    /// <summary>
    /// Executes the given action on the UI thread.
    /// Platform-specific implementations should use their dispatcher mechanism.
    /// </summary>
    protected abstract Task RunOnUiThreadAsync(Func<Task> action);

    /// <summary>
    /// Opens Windows Explorer to the specified drive letter.
    /// Platform-specific implementations may differ in process start options.
    /// </summary>
    protected abstract void OpenExplorerCore(char driveLetter);

    /// <summary>
    /// Opens the history window.
    /// Platform-specific implementations handle window creation and display differently.
    /// </summary>
    protected abstract void OpenHistoryWindowCore();

    #endregion

    #region Property Change Handling

    partial void OnCurrentMountedDiskIndexChanged(int? value)
    {
        OnPropertyChanged(nameof(IsMounted));
    }

    private void OnPropertyChangedHandler(object? sender, PropertyChangedEventArgs e)
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
            _detectedFsType = null;
            OnPropertyChanged(nameof(DetectedFileSystem));
            MountCommand.NotifyCanExecuteChanged();

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

    #endregion

    #region Helper Methods

    /// <summary>
    /// Gets the filesystem type to use for mounting.
    /// </summary>
    protected string GetFileSystemTypeForMount()
    {
        if (!string.IsNullOrEmpty(_detectedFsType))
            return _detectedFsType;
        return "auto";
    }

    /// <summary>
    /// Gets the display string for the detected filesystem.
    /// </summary>
    protected string GetDetectedFileSystem()
    {
        if (SelectedPartition == null)
            return "Select a partition";
        if (IsDetectingFs)
            return "Detecting...";
        if (!string.IsNullOrEmpty(_detectedFsType))
            return _detectedFsType.ToUpperInvariant();
        return "Click Detect to identify";
    }

    private bool CanDetectFilesystem() => SelectedDisk != null && SelectedPartition != null && !IsBusy && !IsDetectingFs;

    private bool CanMount()
    {
        return !IsBusy &&
               !IsMounted &&
               SelectedDisk != null &&
               SelectedPartition != null &&
               SelectedDriveLetter != null;
    }

    private bool CanUnmount()
    {
        return !IsBusy && IsMounted;
    }

    private bool CanOpenExplorerExecute()
    {
        return CanOpenExplorer && CurrentMountedDriveLetter.HasValue;
    }

    /// <summary>
    /// Populates the Partitions collection with partitions from SelectedDisk that are likely Linux partitions.
    /// </summary>
    protected void UpdatePartitions()
    {
        Partitions.Clear();

        if (SelectedDisk == null)
        {
            return;
        }

        var linuxPartitions = SelectedDisk.Partitions
            .Where(p => p.IsLikelyLinux)
            .ToList();

        foreach (var partition in linuxPartitions)
        {
            Partitions.Add(partition);
        }

        SelectedPartition = Partitions.Count > 0 ? Partitions[0] : null;
    }

    /// <summary>
    /// Retrieves disks and drive letters data without touching UI components.
    /// This method is safe to call from any thread.
    /// </summary>
    protected (IEnumerable<DiskInfo> candidateDisks, IEnumerable<DriveLetterInfo> freeLetters) GetDisksAndDriveLettersData()
    {
        var candidateDisks = DiskService.GetCandidateDisks();
        var freeLetters = DriveLetterService.GetFreeLetters()
            .Select(letter => new DriveLetterInfo { Letter = letter, IsInUse = false });

        return (candidateDisks, freeLetters);
    }

    /// <summary>
    /// Updates UI collections and properties with the provided data.
    /// This method must be called on the UI thread.
    /// </summary>
    protected void UpdateUIWithData((IEnumerable<DiskInfo> candidateDisks, IEnumerable<DriveLetterInfo> freeLetters) data)
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
    /// Reads process stdout with proper timeout handling.
    /// </summary>
    protected async Task<string> ReadProcessOutputWithTimeoutAsync(Process process, int timeoutMs, string processName)
    {
        using var cts = new CancellationTokenSource(timeoutMs);
        string output;

        try
        {
            output = await process.StandardOutput.ReadToEndAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(); } catch { /* ignore kill errors */ }
            Logger.LogWarning("{ProcessName} process timed out reading output after {TimeoutMs}ms and was killed", processName, timeoutMs);
            return string.Empty;
        }

        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(); } catch { /* ignore kill errors */ }
            Logger.LogDebug("{ProcessName} process exit wait timed out after output was read; returning captured output", processName);
        }

        return output;
    }

    #endregion

    #region Commands

    /// <summary>
    /// Loads disks and drive letters from the system.
    /// </summary>
    [RelayCommand]
    protected async Task RefreshAsync()
    {
        await LoadDisksAndDriveLettersAsync();
    }

    /// <summary>
    /// Detects the filesystem type of the selected partition using WSL.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanDetectFilesystem))]
    protected async Task DetectFilesystemAsync()
    {
        if (SelectedDisk == null || SelectedPartition == null)
            return;

        IsDetectingFs = true;
        OnPropertyChanged(nameof(DetectedFileSystem));
        StatusMessage = "Detecting filesystem type (UAC prompt required)...";

        try
        {
            var fsType = await FilesystemDetectionService.DetectFilesystemTypeAsync(
                SelectedDisk.Index,
                SelectedPartition.PartitionNumber);

            _detectedFsType = fsType;
            OnPropertyChanged(nameof(DetectedFileSystem));

            StatusMessage = !string.IsNullOrEmpty(fsType)
                ? $"Detected filesystem: {fsType.ToUpperInvariant()}"
                : "Could not detect filesystem type. Will use auto-detection during mount.";
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to detect filesystem type");
            StatusMessage = $"Detection failed: {ex.Message}";
        }
        finally
        {
            IsDetectingFs = false;
            OnPropertyChanged(nameof(DetectedFileSystem));
        }
    }

    /// <summary>
    /// Mounts the selected disk partition into WSL2 and maps it to a Windows drive letter.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanMount))]
    protected async Task MountAsync()
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

            var result = await MountOrchestrator.MountAndMapAsync(
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

            CurrentMountedDiskIndex = SelectedDisk.Index;
            CurrentMountedPartition = SelectedPartition.PartitionNumber;
            var driveLetter = result.DriveLetter ?? SelectedDriveLetter.Value;
            CurrentMountedDriveLetter = driveLetter;
            CanOpenExplorer = true;

            UnmountCommand.NotifyCanExecuteChanged();
            OpenExplorerCommand.NotifyCanExecuteChanged();
            MountCommand.NotifyCanExecuteChanged();

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
                await MountStateService.RegisterMountAsync(activeMount);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Failed to persist mount state for disk {DiskIndex} partition {Partition}", result.DiskIndex, result.Partition);
                StatusMessage = $"Mounted as {SelectedDriveLetter}: - Warning: History not saved due to persistence error.";
                return;
            }

            StatusMessage = $"Success! Mounted as {SelectedDriveLetter}: - You can now access the Linux partition from Windows Explorer.";
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Mount operation failed for disk {DiskIndex} partition {Partition}", SelectedDisk?.Index, SelectedPartition?.PartitionNumber);
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Opens Windows Explorer to the mapped drive letter.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanOpenExplorerExecute))]
    protected void OpenExplorer()
    {
        if (!CurrentMountedDriveLetter.HasValue)
        {
            return;
        }

        try
        {
            OpenExplorerCore(CurrentMountedDriveLetter.Value);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to open Explorer: {ex.Message}";
        }
    }

    /// <summary>
    /// Unmounts the currently mounted disk from WSL2 and unmaps the Windows drive letter.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanUnmount))]
    protected async Task UnmountAsync()
    {
        if (!CurrentMountedDiskIndex.HasValue)
        {
            StatusMessage = "No disk is currently mounted.";
            return;
        }

        var confirmed = await DialogService.ConfirmAsync(
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

            var unmountResult = await UnmountOrchestrator.UnmountAndUnmapAsync(
                CurrentMountedDiskIndex.Value,
                CurrentMountedDriveLetter,
                progress);

            if (!unmountResult.Success)
            {
                StatusMessage = unmountResult.ErrorMessage ?? "Unmount operation failed.";
                return;
            }

            var diskIndex = CurrentMountedDiskIndex.Value;

            try
            {
                await MountStateService.UnregisterMountAsync(diskIndex);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Failed to unregister mount state for disk {DiskIndex}, but proceeding with UI cleanup", diskIndex);
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
            Logger.LogError(ex, "Unmount operation failed for disk {DiskIndex}", CurrentMountedDiskIndex);
            StatusMessage = $"Error during unmount: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Opens the mount history window.
    /// </summary>
    [RelayCommand]
    protected void OpenHistory()
    {
        try
        {
            OpenHistoryWindowCore();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to open history window");
            _ = DialogService.ShowErrorAsync($"Failed to open history window:\n\n{ex.Message}", "Error");
        }
    }

    #endregion

    #region Initialization

    /// <summary>
    /// Initializes the ViewModel by validating the environment and loading disks and drive letters asynchronously.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        StatusMessage = "Validating environment...";
        Logger.LogInformation("Starting application initialization with environment validation");

        var validationResult = await EnvironmentValidationService.ValidateEnvironmentAsync(cancellationToken);

        if (!validationResult.IsValid)
        {
            Logger.LogWarning("Environment validation failed. Errors: {Errors}", string.Join("; ", validationResult.Errors));

            var errorMessage = "LiMount cannot start because your system does not meet the requirements:\n\n";
            errorMessage += string.Join("\n", validationResult.Errors.Select(e => $"• {e}"));
            errorMessage += "\n\nTo fix these issues:\n\n";
            errorMessage += string.Join("\n", validationResult.Suggestions.Select(s => $"  {s}"));

            await DialogService.ShowErrorAsync(errorMessage, "Environment Validation Failed");

            StatusMessage = "Environment validation failed. Please check the requirements.";
            return;
        }

        Logger.LogInformation("Environment validation successful. WSL distros: {Distros}",
            string.Join(", ", validationResult.InstalledDistros));

        if (Config.Initialization.AutoReconcileMounts)
        {
            StatusMessage = "Reconciling mount state...";
            try
            {
                var orphanedMounts = await MountStateService.ReconcileMountStateAsync(cancellationToken);
                if (orphanedMounts.Count > 0)
                {
                    Logger.LogInformation("Reconciliation found {Count} orphaned mount(s)", orphanedMounts.Count);
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Mount state reconciliation failed, continuing with initialization");
            }
        }

        StatusMessage = $"Environment OK. Found {validationResult.InstalledDistros.Count} WSL distro(s). Loading disks...";

        var data = await Task.Run(GetDisksAndDriveLettersData, cancellationToken);

        await RunOnUiThreadAsync(() =>
        {
            UpdateUIWithData(data);
            return Task.CompletedTask;
        });

        await DetectExistingMountsAsync(cancellationToken);
    }

    /// <summary>
    /// Loads candidate disks and available drive letters.
    /// </summary>
    protected async Task LoadDisksAndDriveLettersAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            StatusMessage = "Loading disks and drive letters...";

            var data = await Task.Run(GetDisksAndDriveLettersData, cancellationToken);

            await RunOnUiThreadAsync(() =>
            {
                UpdateUIWithData(data);
                return Task.CompletedTask;
            });
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to load disks and drive letters");
            StatusMessage = $"Error loading disks: {ex.Message}";
        }
    }

    /// <summary>
    /// Detects any existing WSL mounts and drive letter mappings from previous sessions.
    /// </summary>
    protected async Task DetectExistingMountsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            StatusMessage = "Checking for existing mounts...";

            var activeMounts = await MountStateService.GetActiveMountsAsync(cancellationToken);
            if (activeMounts.Count > 0)
            {
                var mount = activeMounts.First();

                var uncPathExists = false;
                if (!string.IsNullOrEmpty(mount.MountPathUNC))
                {
                    var timeoutMs = Config.MountOperations.UncPathCheckTimeoutMs;
                    try
                    {
                        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                        cts.CancelAfter(timeoutMs);
                        var checkTask = Task.Run(() => Directory.Exists(mount.MountPathUNC), cts.Token);
                        uncPathExists = await checkTask.WaitAsync(cts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        Logger.LogWarning("UNC path check timed out after {TimeoutMs}ms for {UNC}. " +
                            "The underlying I/O may still be blocked on a dead network path.", timeoutMs, mount.MountPathUNC);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning(ex, "Failed to verify UNC path {UNC}", mount.MountPathUNC);
                    }
                }

                if (uncPathExists)
                {
                    CurrentMountedDiskIndex = mount.DiskIndex;
                    CurrentMountedPartition = mount.PartitionNumber;
                    CurrentMountedDriveLetter = mount.DriveLetter;
                    CanOpenExplorer = true;

                    StatusMessage = $"Found existing mount: Disk {mount.DiskIndex} partition {mount.PartitionNumber} → {mount.DriveLetter}:";
                    Logger.LogInformation("Detected existing mount from state: Disk {DiskIndex} partition {Partition} at {DriveLetter}:",
                        mount.DiskIndex, mount.PartitionNumber, mount.DriveLetter);

                    UnmountCommand.NotifyCanExecuteChanged();
                    OpenExplorerCommand.NotifyCanExecuteChanged();
                    MountCommand.NotifyCanExecuteChanged();
                    return;
                }
                else
                {
                    Logger.LogInformation("Stale mount state found for disk {DiskIndex}, cleaning up", mount.DiskIndex);
                    await MountStateService.UnregisterMountAsync(mount.DiskIndex, cancellationToken);
                }
            }

            var detectedMount = await DetectMountFromSystemAsync(cancellationToken);
            if (detectedMount != null)
            {
                CurrentMountedDiskIndex = detectedMount.Value.diskIndex;
                CurrentMountedPartition = detectedMount.Value.partition;

                if (detectedMount.Value.driveLetter != '\0')
                {
                    CurrentMountedDriveLetter = detectedMount.Value.driveLetter;
                    CanOpenExplorer = true;
                    StatusMessage = $"Detected existing mount: Disk {detectedMount.Value.diskIndex} → {detectedMount.Value.driveLetter}:";
                }
                else
                {
                    CurrentMountedDriveLetter = null;
                    CanOpenExplorer = false;
                    StatusMessage = $"Detected WSL mount for Disk {detectedMount.Value.diskIndex} (no drive letter). Click Unmount to clean up.";
                }

                Logger.LogInformation("Detected existing mount from system: Disk {DiskIndex} partition {Partition}, drive letter: {DriveLetter}",
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
            Logger.LogWarning(ex, "Failed to detect existing mounts");
            StatusMessage = $"Found {Disks.Count} candidate disk(s). Ready to mount.";
        }
    }

    /// <summary>
    /// Checks the system for existing WSL mounts and subst mappings.
    /// Uses regex timeouts and input validation to prevent security issues.
    /// </summary>
    protected async Task<(int diskIndex, int partition, char driveLetter)?> DetectMountFromSystemAsync(CancellationToken cancellationToken = default)
    {
        // Check subst mappings
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
                var substTimeoutMs = Config.MountOperations.SubstCommandTimeoutMs;
                var output = await ReadProcessOutputWithTimeoutAsync(process, substTimeoutMs, "subst");

                foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    var match = Regex.Match(
                        line,
                        @"^([A-Z]):\\: => .+PHYSICALDRIVE(\d+)p(\d+)",
                        RegexOptions.IgnoreCase,
                        TimeSpan.FromMilliseconds(RegexTimeoutMs));

                    if (match.Success)
                    {
                        var driveLetter = match.Groups[1].Value[0];

                        if (!int.TryParse(match.Groups[2].Value, out var diskIndex) || diskIndex < 0 || diskIndex > MaxDiskIndex)
                        {
                            Logger.LogWarning("Invalid disk index in subst output: {Value}", match.Groups[2].Value);
                            continue;
                        }
                        if (!int.TryParse(match.Groups[3].Value, out var partition) || partition < 0 || partition > MaxPartition)
                        {
                            Logger.LogWarning("Invalid partition number in subst output: {Value}", match.Groups[3].Value);
                            continue;
                        }

                        Logger.LogDebug("Found subst mapping: {DriveLetter}: -> Disk {DiskIndex} partition {Partition}",
                            driveLetter, diskIndex, partition);

                        return (diskIndex, partition, driveLetter);
                    }
                }
            }
        }
        catch (RegexMatchTimeoutException ex)
        {
            Logger.LogWarning(ex, "Regex timeout while parsing subst output");
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to check subst mappings");
        }

        // Check for WSL-only mounts (no drive letter mapping)
        try
        {
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
                var wslTimeoutMs = Config.MountOperations.WslCommandTimeoutMs;
                var wslOutput = await ReadProcessOutputWithTimeoutAsync(wslProcess, wslTimeoutMs, "wsl ls");

                foreach (var entry in wslOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    var match = Regex.Match(
                        entry.Trim(),
                        @"PHYSICALDRIVE(\d+)p(\d+)",
                        RegexOptions.IgnoreCase,
                        TimeSpan.FromMilliseconds(RegexTimeoutMs));

                    if (match.Success)
                    {
                        if (!int.TryParse(match.Groups[1].Value, out var diskIndex) || diskIndex < 0 || diskIndex > MaxDiskIndex)
                        {
                            Logger.LogWarning("Invalid disk index in WSL output: {Value}", match.Groups[1].Value);
                            continue;
                        }
                        if (!int.TryParse(match.Groups[2].Value, out var partition) || partition < 0 || partition > MaxPartition)
                        {
                            Logger.LogWarning("Invalid partition number in WSL output: {Value}", match.Groups[2].Value);
                            continue;
                        }

                        Logger.LogDebug("Found WSL mount without drive letter: Disk {DiskIndex} partition {Partition}",
                            diskIndex, partition);

                        return (diskIndex, partition, '\0');
                    }
                }
            }
        }
        catch (RegexMatchTimeoutException ex)
        {
            Logger.LogWarning(ex, "Regex timeout while parsing WSL output");
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to check WSL mounts");
        }

        return null;
    }

    #endregion
}
