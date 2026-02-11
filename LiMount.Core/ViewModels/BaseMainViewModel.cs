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
    private ObservableCollection<DiskInfo> _disks = [];

    [ObservableProperty]
    private DiskInfo? _selectedDisk;

    [ObservableProperty]
    private ObservableCollection<PartitionInfo> _partitions = [];

    [ObservableProperty]
    private PartitionInfo? _selectedPartition;

    [ObservableProperty]
    private ObservableCollection<char> _freeDriveLetters = [];

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
    [NotifyPropertyChangedFor(nameof(IsMounted))]
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

    protected sealed record Dependencies(
        IDiskEnumerationService DiskService,
        IDriveLetterService DriveLetterService,
        IMountOrchestrator MountOrchestrator,
        IUnmountOrchestrator UnmountOrchestrator,
        IMountStateService MountStateService,
        IEnvironmentValidationService EnvironmentValidationService,
        IFilesystemDetectionService FilesystemDetectionService,
        IDialogService DialogService,
        ILogger Logger,
        LiMountConfiguration Config);

    /// <summary>
    /// Initializes a new instance of <see cref="BaseMainViewModel"/> with required services.
    /// </summary>
    protected BaseMainViewModel(Dependencies deps)
    {
        ArgumentNullException.ThrowIfNull(deps);

        var (
            diskService,
            driveLetterService,
            mountOrchestrator,
            unmountOrchestrator,
            mountStateService,
            environmentValidationService,
            filesystemDetectionService,
            dialogService,
            logger,
            config) = deps;

        ArgumentNullException.ThrowIfNull(diskService);
        ArgumentNullException.ThrowIfNull(driveLetterService);
        ArgumentNullException.ThrowIfNull(mountOrchestrator);
        ArgumentNullException.ThrowIfNull(unmountOrchestrator);
        ArgumentNullException.ThrowIfNull(mountStateService);
        ArgumentNullException.ThrowIfNull(environmentValidationService);
        ArgumentNullException.ThrowIfNull(filesystemDetectionService);
        ArgumentNullException.ThrowIfNull(dialogService);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(config);

        DiskService = diskService;
        DriveLetterService = driveLetterService;
        MountOrchestrator = mountOrchestrator;
        UnmountOrchestrator = unmountOrchestrator;
        MountStateService = mountStateService;
        EnvironmentValidationService = environmentValidationService;
        FilesystemDetectionService = filesystemDetectionService;
        DialogService = dialogService;
        Logger = logger;
        Config = config;

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

    private bool CanDetectFilesystem() => SelectedDisk != null && SelectedPartition != null && !IsBusy && !IsDetectingFs && FilesystemDetectionService != null;

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

        if (Disks.Count == 0)
        {
            StatusMessage = "No Linux candidate disks found. Connect a Linux disk and click Refresh.";
            return;
        }

        if (FreeDriveLetters.Count == 0)
        {
            StatusMessage = "No free drive letters are available. Unmap a drive and click Refresh.";
            return;
        }

        StatusMessage = $"Found {Disks.Count} candidate disk(s) and {FreeDriveLetters.Count} free drive letter(s).";
    }

    /// <summary>
    /// Reads process stdout with proper timeout handling.
    /// </summary>
    protected async Task<string> ReadProcessOutputWithTimeoutAsync(Process process, int timeoutMs, string processName, CancellationToken cancellationToken = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeoutMs);
        string output;

        try
        {
            output = await process.StandardOutput.ReadToEndAsync(cts.Token);
        }
        catch (OperationCanceledException ex)
        {
            try { process.Kill(); } catch { /* ignore kill errors */ }
            Logger.LogWarning(ex, "{ProcessName} process timed out reading output after {TimeoutMs}ms and was killed", processName, timeoutMs);
            return string.Empty;
        }

        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException ex)
        {
            try { process.Kill(); } catch { /* ignore kill errors */ }
            Logger.LogDebug(ex, "{ProcessName} process exit wait timed out after output was read; returning captured output", processName);
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
                MountedAt = DateTime.UtcNow,
                DiskIndex = result.DiskIndex,
                PartitionNumber = result.Partition,
                DriveLetter = driveLetter,
                DistroName = result.DistroName ?? string.Empty,
                MountPathLinux = result.MountPathLinux ?? string.Empty,
                MountPathUNC = result.MountPathUNC ?? string.Empty,
                IsVerified = true,
                LastVerified = DateTime.UtcNow
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
            var partition = CurrentMountedPartition;

            try
            {
                if (partition.HasValue)
                {
                    await MountStateService.UnregisterMountAsync(diskIndex, partition.Value);
                }
                else
                {
                    await MountStateService.UnregisterDiskAsync(diskIndex);
                }
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

            if (await TryRestoreMountFromStateAsync(cancellationToken))
            {
                return;
            }

            if (Config.Initialization.AutoDetectSystemMounts)
            {
                var detectedMount = await DetectMountFromSystemAsync(cancellationToken);
                if (detectedMount is not null)
                {
                    ApplyDetectedMountFromSystem(detectedMount.Value);
                    return;
                }
            }

            SetReadyToMountStatus();
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to detect existing mounts");
            SetReadyToMountStatus();
        }
    }

    private void SetReadyToMountStatus()
    {
        StatusMessage = $"Found {Disks.Count} candidate disk(s). Ready to mount.";
    }

    private void NotifyMountStateCommandsChanged()
    {
        UnmountCommand.NotifyCanExecuteChanged();
        OpenExplorerCommand.NotifyCanExecuteChanged();
        MountCommand.NotifyCanExecuteChanged();
    }

    private async Task<bool> TryRestoreMountFromStateAsync(CancellationToken cancellationToken)
    {
        var activeMounts = await MountStateService.GetActiveMountsAsync(cancellationToken);
        if (activeMounts.Count == 0)
        {
            return false;
        }

        ActiveMount? mountToRestore = null;
        var staleMounts = new List<ActiveMount>();

        foreach (var mount in activeMounts)
        {
            var uncPathExists = await VerifyUncPathExistsAsync(mount, cancellationToken);
            if (uncPathExists)
            {
                mountToRestore ??= mount;
            }
            else
            {
                staleMounts.Add(mount);
            }
        }

        foreach (var staleMount in staleMounts)
        {
            Logger.LogInformation(
                "Stale mount state found for disk {DiskIndex} partition {Partition}, cleaning up entry",
                staleMount.DiskIndex,
                staleMount.PartitionNumber);

            await MountStateService.UnregisterMountAsync(
                staleMount.DiskIndex,
                staleMount.PartitionNumber,
                cancellationToken);
        }

        if (mountToRestore != null)
        {
            ApplyExistingMountFromState(mountToRestore);
            return true;
        }

        return false;
    }

    private async Task<bool> VerifyUncPathExistsAsync(ActiveMount mount, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(mount.MountPathUNC))
        {
            return false;
        }

        var timeoutMs = Config.MountOperations.UncPathCheckTimeoutMs;
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeoutMs);
            var checkTask = Task.Run(() => Directory.Exists(mount.MountPathUNC), cts.Token);
            return await checkTask.WaitAsync(cts.Token);
        }
        catch (OperationCanceledException ex)
        {
            Logger.LogWarning(ex, "UNC path check timed out after {TimeoutMs}ms for {UNC}. The underlying I/O may still be blocked on a dead network path.", timeoutMs, mount.MountPathUNC);
            return false;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to verify UNC path {UNC}", mount.MountPathUNC);
            return false;
        }
    }

    private void ApplyExistingMountFromState(ActiveMount mount)
    {
        CurrentMountedDiskIndex = mount.DiskIndex;
        CurrentMountedPartition = mount.PartitionNumber;
        CurrentMountedDriveLetter = mount.DriveLetter;
        CanOpenExplorer = true;

        StatusMessage = $"Found existing mount: Disk {mount.DiskIndex} partition {mount.PartitionNumber} → {mount.DriveLetter}:";
        Logger.LogInformation("Detected existing mount from state: Disk {DiskIndex} partition {Partition} at {DriveLetter}:",
            mount.DiskIndex, mount.PartitionNumber, mount.DriveLetter);

        NotifyMountStateCommandsChanged();
    }

    private void ApplyDetectedMountFromSystem((int diskIndex, int partition, char driveLetter) detectedMount)
    {
        CurrentMountedDiskIndex = detectedMount.diskIndex;
        CurrentMountedPartition = detectedMount.partition;

        if (detectedMount.driveLetter != '\0')
        {
            CurrentMountedDriveLetter = detectedMount.driveLetter;
            CanOpenExplorer = true;
            StatusMessage = $"Detected existing mount: Disk {detectedMount.diskIndex} → {detectedMount.driveLetter}:";
        }
        else
        {
            CurrentMountedDriveLetter = null;
            CanOpenExplorer = false;
            StatusMessage = $"Detected WSL mount for Disk {detectedMount.diskIndex} (no drive letter). Click Unmount to clean up.";
        }

        Logger.LogInformation("Detected existing mount from system: Disk {DiskIndex} partition {Partition}, drive letter: {DriveLetter}",
            detectedMount.diskIndex, detectedMount.partition,
            detectedMount.driveLetter == '\0' ? "(none)" : detectedMount.driveLetter.ToString());

        NotifyMountStateCommandsChanged();
    }

    /// <summary>
    /// Checks the system for existing WSL mounts and subst mappings.
    /// Uses regex timeouts and input validation to prevent security issues.
    /// </summary>
    protected async Task<(int diskIndex, int partition, char driveLetter)?> DetectMountFromSystemAsync(CancellationToken cancellationToken = default)
    {
        var substMount = await TryDetectMountFromSubstAsync(cancellationToken);
        if (substMount is not null)
        {
            return substMount;
        }

        return await TryDetectMountFromWslAsync(cancellationToken);
    }

    private static bool TryParseDiskIndex(string value, out int diskIndex)
    {
        return int.TryParse(value, out diskIndex) && diskIndex >= 0 && diskIndex <= MaxDiskIndex;
    }

    private static bool TryParsePartition(string value, out int partition)
    {
        return int.TryParse(value, out partition) && partition >= 0 && partition <= MaxPartition;
    }

    internal static (int diskIndex, int partition)? ParseMountedPhysicalDriveFromMountOutput(string wslOutput)
    {
        foreach (var entry in wslOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var match = Regex.Match(
                entry,
                @"/mnt/wsl/PHYSICALDRIVE(\d+)p(\d+)\b",
                RegexOptions.IgnoreCase,
                TimeSpan.FromMilliseconds(RegexTimeoutMs));

            if (!match.Success)
            {
                continue;
            }

            if (!TryParseDiskIndex(match.Groups[1].Value, out var diskIndex))
            {
                continue;
            }

            if (!TryParsePartition(match.Groups[2].Value, out var partition))
            {
                continue;
            }

            return (diskIndex, partition);
        }

        return null;
    }

    internal static IReadOnlyList<string> FindStalePhysicalDriveDirectories(string directoryListingOutput, string mountOutput)
    {
        var listedEntries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in directoryListingOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (TryParsePhysicalDriveEntryName(entry, out var normalizedName))
            {
                listedEntries.Add(normalizedName);
            }
        }

        var mountedEntries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in mountOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var match = Regex.Match(
                entry,
                @"/mnt/wsl/(PHYSICALDRIVE(\d+)p(\d+))\b",
                RegexOptions.IgnoreCase,
                TimeSpan.FromMilliseconds(RegexTimeoutMs));

            if (!match.Success)
            {
                continue;
            }

            if (!TryParseDiskIndex(match.Groups[2].Value, out var diskIndex))
            {
                continue;
            }

            if (!TryParsePartition(match.Groups[3].Value, out var partition))
            {
                continue;
            }

            mountedEntries.Add($"PHYSICALDRIVE{diskIndex}p{partition}");
        }

        return listedEntries
            .Where(entry => !mountedEntries.Contains(entry))
            .OrderBy(entry => entry, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool TryParsePhysicalDriveEntryName(string entry, out string normalizedName)
    {
        normalizedName = string.Empty;
        var match = Regex.Match(
            entry,
            @"^PHYSICALDRIVE(\d+)p(\d+)$",
            RegexOptions.IgnoreCase,
            TimeSpan.FromMilliseconds(RegexTimeoutMs));

        if (!match.Success)
        {
            return false;
        }

        if (!TryParseDiskIndex(match.Groups[1].Value, out var diskIndex))
        {
            return false;
        }

        if (!TryParsePartition(match.Groups[2].Value, out var partition))
        {
            return false;
        }

        normalizedName = $"PHYSICALDRIVE{diskIndex}p{partition}";
        return true;
    }

    private async Task<(int diskIndex, int partition, char driveLetter)?> TryDetectMountFromSubstAsync(CancellationToken cancellationToken)
    {
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
            if (process == null)
            {
                return null;
            }

            var substTimeoutMs = Config.MountOperations.SubstCommandTimeoutMs;
            var output = await ReadProcessOutputWithTimeoutAsync(process, substTimeoutMs, "subst", cancellationToken);

            foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var match = Regex.Match(
                    line,
                    @"^([A-Z]):\\: => .+PHYSICALDRIVE(\d+)p(\d+)",
                    RegexOptions.IgnoreCase,
                    TimeSpan.FromMilliseconds(RegexTimeoutMs));

                if (!match.Success)
                {
                    continue;
                }

                var driveLetter = match.Groups[1].Value[0];

                if (!TryParseDiskIndex(match.Groups[2].Value, out var diskIndex))
                {
                    Logger.LogWarning("Invalid disk index in subst output: {Value}", match.Groups[2].Value);
                    continue;
                }

                if (!TryParsePartition(match.Groups[3].Value, out var partition))
                {
                    Logger.LogWarning("Invalid partition number in subst output: {Value}", match.Groups[3].Value);
                    continue;
                }

                Logger.LogDebug("Found subst mapping: {DriveLetter}: -> Disk {DiskIndex} partition {Partition}",
                    driveLetter, diskIndex, partition);

                return (diskIndex, partition, driveLetter);
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

        return null;
    }

    private async Task<(int diskIndex, int partition, char driveLetter)?> TryDetectMountFromWslAsync(CancellationToken cancellationToken)
    {
        try
        {
            var wslPsi = new ProcessStartInfo
            {
                FileName = "wsl.exe",
                // IMPORTANT: list actual mounted filesystems, not directory names under /mnt/wsl.
                // Stale directories (for example PHYSICALDRIVEXpY) can persist after failed mounts and
                // would cause false "already mounted" detections if we only run `ls /mnt/wsl`.
                Arguments = "-e sh -lc \"mount | grep -i '/mnt/wsl/PHYSICALDRIVE' || true\"",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var wslProcess = Process.Start(wslPsi);
            if (wslProcess == null)
            {
                return null;
            }

            var wslTimeoutMs = Config.MountOperations.WslCommandTimeoutMs;
            var wslOutput = await ReadProcessOutputWithTimeoutAsync(wslProcess, wslTimeoutMs, "wsl mount query", cancellationToken);

            var parsedMount = ParseMountedPhysicalDriveFromMountOutput(wslOutput);
            if (parsedMount.HasValue)
            {
                var (diskIndex, partition) = parsedMount.Value;
                Logger.LogDebug("Found WSL mount without drive letter: Disk {DiskIndex} partition {Partition}",
                    diskIndex, partition);

                return (diskIndex, partition, '\0');
            }

            // Best-effort self-heal: prune stale empty /mnt/wsl/PHYSICALDRIVE*p* directories
            // that are not present in the live mount table.
            var listPsi = new ProcessStartInfo
            {
                FileName = "wsl.exe",
                Arguments = "-e ls /mnt/wsl",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var listProcess = Process.Start(listPsi);
            if (listProcess == null)
            {
                return null;
            }

            var listingOutput = await ReadProcessOutputWithTimeoutAsync(listProcess, wslTimeoutMs, "wsl stale dir query", cancellationToken);
            var staleEntries = FindStalePhysicalDriveDirectories(listingOutput, wslOutput);
            if (staleEntries.Count > 0)
            {
                Logger.LogInformation("Detected {Count} stale WSL mount directory entries: {Entries}", staleEntries.Count, string.Join(", ", staleEntries));
                await TryCleanupStalePhysicalDriveDirectoriesAsync(staleEntries, wslOutput, cancellationToken);
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

    private async Task TryCleanupStalePhysicalDriveDirectoriesAsync(
        IReadOnlyList<string> staleEntries,
        string mountOutput,
        CancellationToken cancellationToken)
    {
        if (staleEntries.Count == 0)
        {
            return;
        }

        // Entries are validated by FindStalePhysicalDriveDirectories and reconstructed in canonical form.
        var entriesArg = string.Join(" ", staleEntries.Select(entry => $"'{entry}'"));
        var cleanupScript = $"for d in {entriesArg}; do p=\"/mnt/wsl/$d\"; [ -d \"$p\" ] || continue; mount | grep -F \" on $p \" >/dev/null 2>&1 && continue; rmdir \"$p\" 2>/dev/null || true; done";

        var cleanupPsi = new ProcessStartInfo
        {
            FileName = "wsl.exe",
            Arguments = $"-e sh -lc \"{cleanupScript}\"",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var cleanupProcess = Process.Start(cleanupPsi);
        if (cleanupProcess == null)
        {
            Logger.LogWarning("Failed to start WSL stale-directory cleanup process.");
            return;
        }

        var timeoutMs = Config.MountOperations.WslCommandTimeoutMs;
        await ReadProcessOutputWithTimeoutAsync(cleanupProcess, timeoutMs, "wsl stale dir cleanup", cancellationToken);

        var verifyPsi = new ProcessStartInfo
        {
            FileName = "wsl.exe",
            Arguments = "-e ls /mnt/wsl",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var verifyProcess = Process.Start(verifyPsi);
        if (verifyProcess == null)
        {
            return;
        }

        var verifyOutput = await ReadProcessOutputWithTimeoutAsync(verifyProcess, timeoutMs, "wsl stale dir verify", cancellationToken);
        var remainingStale = FindStalePhysicalDriveDirectories(verifyOutput, mountOutput);
        var stillPresent = staleEntries.Where(entry => remainingStale.Contains(entry, StringComparer.OrdinalIgnoreCase)).ToArray();

        if (stillPresent.Length == 0)
        {
            Logger.LogInformation("Successfully removed stale WSL directory entries: {Entries}", string.Join(", ", staleEntries));
        }
        else
        {
            Logger.LogWarning(
                "Stale WSL directory cleanup could not remove: {Entries}. This is non-fatal and may indicate non-empty folders or insufficient permissions.",
                string.Join(", ", stillPresent));
        }
    }

    #endregion
}
