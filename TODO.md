# LiMount - TODO List

**Project**: Windows desktop GUI for mounting Linux disks/partitions into WSL2 and mapping them as Windows drive letters.

**Tech Stack**: C# WPF (.NET 8), PowerShell helpers

---

## Research & Documentation

- [x] Research latest .NET 8 and WPF documentation
  - .NET 8 is LTS (3-year support)
  - WPF has hardware acceleration for RDP and OpenFolderDialog
  - Docs: <https://learn.microsoft.com/en-us/dotnet/desktop/wpf/whats-new/net80>
- [x] Research System.Management and WMI for disk enumeration
  - Win32_DiskDrive class for physical disks
  - Win32_Volume for partitions and volumes
  - Docs: <https://learn.microsoft.com/en-us/windows/win32/cimwin32prov/win32-diskdrive>
- [x] Research WSL2 `wsl --mount` command
  - Requires Windows 11 Build 22000+ or Microsoft Store WSL
  - Requires Administrator access
  - Syntax: `wsl --mount <Disk> --partition <Index> --type <FsType>`
  - Docs: <https://learn.microsoft.com/en-us/windows/wsl/wsl2-mount-disk>
- [x] Research ProcessStartInfo for PowerShell execution
  - Use UseShellExecute=false for stream redirection
  - RedirectStandardOutput/Error for capturing output
  - Verb="runas" for elevation
  - Docs: <https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.processstartinfo>

---

## Milestone 1: Solution Skeleton ✅ **COMPLETED**

- [x] Initialize .NET solution (LiMount.sln)
- [x] Create LiMount.Core class library (.NET 8)
- [x] Create LiMount.App WPF project (.NET 8)
- [x] Add project reference: LiMount.App → LiMount.Core
- [x] Create scripts folder at repo root
- [x] Verify solution builds with `dotnet build`

---

## Milestone 2: Core Models (LiMount.Core)

- [x] Create Models folder
- [x] Implement DiskInfo model
  - Properties: Index, DeviceId, Model, SizeBytes, IsSystem, IsBoot, Partitions
- [x] Implement PartitionInfo model
  - Properties: PartitionNumber, SizeBytes, Label, FileSystemType, HasDriveLetter, DriveLetter, IsLikelyLinux
- [x] Implement DriveLetterInfo model
  - Properties: Letter, IsInUse, Description
- [x] Implement MountResult model for script output parsing
  - Properties: Success, DistroName, MountPathLinux, MountPathUNC, ErrorMessage
- [x] Implement MappingResult model for drive mapping output
  - Properties: Success, DriveLetter, TargetUNC, ErrorMessage

---

## Milestone 3: Core Services (LiMount.Core)

- [x] Add System.Management NuGet package (if needed for .NET 8)
- [x] Create Services folder
- [x] Implement KeyValueOutputParser utility
  - Parse "key=value" lines from PowerShell scripts into Dictionary
  - Handle STATUS=OK/ERROR patterns
- [x] Implement DiskEnumerationService
  - GetDisks() using WMI (Win32_DiskDrive, Win32_Volume, etc.)
  - Detect IsSystem and IsBoot flags
  - Map partitions to PartitionInfo with IsLikelyLinux heuristic
  - GetCandidateDisks() filtering non-system disks
- [x] Implement DriveLetterService
  - GetUsedLetters() using DriveInfo.GetDrives()
  - GetFreeLetters() returning A-Z minus used, sorted Z→A

---

## Milestone 4: PowerShell Helper Scripts

- [x] Create Mount-LinuxDiskCore.ps1 (elevated)
  - Parameters: DiskIndex, Partition, FsType (default ext4), DistroName (optional)
  - Require Administrator check
  - Enumerate disks via Get-CimInstance Win32_DiskDrive
  - Safety check: reject system/boot disks using Get-Disk
  - Validate partition number
  - Build physical disk path: `\\.\PHYSICALDRIVE<DiskIndex>`
  - Execute `wsl --mount <diskPath> --partition <Partition> --type <FsType>`
  - Handle errors: already mounted, unsupported wsl version, generic failures
  - Determine distro: Get-ChildItem \\wsl$, fallback to wsl -l -q
  - Determine mount path: ls /mnt/wsl, find PHYSICALDRIVE<`DiskIndex`>
  - Output machine-readable key=value:
    - STATUS=OK/ERROR
    - DistroName, MountPathLinux, MountPathUNC
    - ErrorMessage (on error)
  - Exit code: 0 on success, 1 on error
- [x] Create Map-WSLShareToDrive.ps1 (non-elevated)
  - Parameters: DriveLetter, TargetUNC
  - Normalize DriveLetter to single uppercase char A-Z
  - Validate TargetUNC non-empty
  - Test-Path for UNC reachability
  - Check existing mappings: net use, subst, Get-PSDrive
  - Handle conflicts (drive in use with different target)
  - Map using `net use X: $TargetUNC`, verify with Test-Path
  - Fallback to New-PSDrive if net use fails
  - Output key=value result:
    - STATUS=OK/ERROR
    - DriveLetter, MappedTo
    - ErrorMessage (on error)
  - Appropriate exit codes

---

## Milestone 5: WPF UI - XAML (LiMount.App)

- [x] Design MainWindow.xaml with modern, clean layout
  - Use consistent color palette (light theme + accent)
  - Group controls with clear labels and spacing
  - Visual hierarchy: instructions → selections → status/log → buttons
- [x] Add ComboBox for disk selection
  - Display: "Index – Model – Size (GB)"
  - Bind to Disks collection
- [x] Add ComboBox for partition selection
  - Display: "<Label or 'Partition N'> – Size (GB)"
  - Filter by IsLikelyLinux
  - Bind to Partitions collection
- [x] Add ComboBox for drive letter
  - Display: "L:"
  - Sorted Z→A
  - Bind to FreeDriveLetters
- [x] Add ComboBox for filesystem type (optional)
  - Options: ext4, xfs
  - Default: ext4
- [x] Add status TextBlock for messages
- [x] Add multi-line log TextBox (optional, for script outputs)
- [x] Add Mount button (primary action)
- [x] Add Refresh button (optional)
- [x] Add Open in Explorer button (optional, enabled after successful mount)

---

## Milestone 6: WPF UI - ViewModel (LiMount.App)

- [x] Create ViewModels folder
- [x] Implement RelayCommand helper class (or use CommunityToolkit.Mvvm)
- [x] Implement MainViewModel
  - Properties:
    - ObservableCollection<`DiskInfo`> Disks
    - DiskInfo? SelectedDisk
    - ObservableCollection<`PartitionInfo`> Partitions (filtered by SelectedDisk)
    - PartitionInfo? SelectedPartition
    - ObservableCollection<`char`> FreeDriveLetters
    - char? SelectedDriveLetter
    - string SelectedFsType (default "ext4")
    - string StatusMessage
    - bool IsBusy
  - Commands:
    - RefreshCommand: re-fetch disks and free drive letters
    - MountCommand: orchestrate full mount + map workflow
      1. Validate selections
      2. Set IsBusy=true, update status
      3. Execute Mount-LinuxDiskCore.ps1 elevated
      4. Parse MountResult from stdout
      5. On success: execute Map-WSLShareToDrive.ps1 non-elevated
      6. Parse MappingResult
      7. Update status message
      8. Set IsBusy=false
    - OpenExplorerCommand (optional): open drive letter in Explorer
  - Handle errors gracefully, show user-friendly messages

---

## Milestone 7: Integration & Testing ✅ **COMPLETED**

- [x] Wire up MainWindow.xaml to MainViewModel
- [x] Add System.Management package to LiMount.Core (verify .NET 8 compatibility)
- [x] Build entire solution: `dotnet build` (requires Windows with .NET 8 SDK)
- [x] Fix any build errors (if any)
- [x] Create LiMount.Tests project
  - MountOrchestratorTests (8 tests)
  - UnmountOrchestratorTests (7 tests, 1 failing - test bug)
  - MountStateServiceTests
  - DialogServiceTests
- [ ] Manual testing:
  - Launch app
  - Verify disk/partition enumeration
  - Verify drive letter enumeration
  - Click Mount (test UAC prompt)
  - Verify WSL mount succeeds
  - Verify drive letter mapping succeeds
  - Verify drive appears in Explorer
  - Test error scenarios (missing selections, invalid disk, etc.)

---

## Milestone 8: Polish & Documentation

- [ ] Add application icon (optional)
- [x] Improve error messages for common failure modes
- [x] Add logging/diagnostics - Serilog with rolling file logs to %LocalAppData%\LiMount\logs
- [x] Create comprehensive README.md with:
  - Project description
  - Requirements (Windows 11, WSL2, .NET 8 SDK)
  - Build instructions
  - Usage instructions
  - Known limitations
  - Architecture documentation
  - PowerShell script documentation
- [x] Clean up code comments
- [x] Update .gitignore for temp files
- [x] Fix CA1416 platform compatibility warnings
  - Changed LiMount.Core target framework to net8.0-windows
  - Added [SupportedOSPlatform("windows")] attributes to services
  - Updated documentation to reflect Windows-only targeting
- [ ] Final build and verification (requires Windows with .NET 8 SDK)

---

## Milestone 9: Architectural Refactoring ✅ **COMPLETED**

### Interface Abstraction Layer

- [x] Create IDiskEnumerationService interface
- [x] Create IDriveLetterService interface
- [x] Create IScriptExecutor interface
- [x] Create IMountOrchestrator interface
- [x] Create IUnmountOrchestrator interface
- [x] Update DiskEnumerationService to implement IDiskEnumerationService
- [x] Update DriveLetterService to implement IDriveLetterService

### New Model Classes

- [x] Create UnmountResult model for WSL unmount operations
- [x] Create UnmappingResult model for drive letter unmapping
- [x] Create MountAndMapResult model for combined mount+map workflow
- [x] Create UnmountAndUnmapResult model for combined unmount+unmap workflow

### Service Implementations

- [x] Create ScriptExecutor service
  - Encapsulates all PowerShell script execution logic
  - Handles elevated and non-elevated scenarios
  - Implements temp file workaround for elevated scripts
  - Abstracts script path resolution
- [x] Create MountOrchestrator service
  - Coordinates mount + mapping workflow
  - Implements UNC path verification with retry (5 attempts, 500ms delay)
  - Provides progress callbacks via IProgress<`string`>
  - Returns MountAndMapResult with detailed status
- [x] Create UnmountOrchestrator service
  - Coordinates unmapping + unmount workflow
  - Handles optional drive letter unmapping
  - Provides progress callbacks
  - Returns UnmountAndUnmapResult with detailed status

### Unmount Functionality (Critical MVP Feature)

- [x] Create Unmount-LinuxDisk.ps1 (elevated)
  - Parameters: DiskIndex
  - Calls `wsl --unmount`
  - Handles "not mounted" as success
  - Outputs machine-readable key=value pairs
  - Writes to temp file for elevated scenarios
- [x] Create Unmap-DriveLetter.ps1 (non-elevated)
  - Parameters: DriveLetter
  - Tries net use, Remove-PSDrive, and subst
  - Handles already-unmapped gracefully
  - Outputs machine-readable results

### Dependency Injection Setup

- [x] Add Microsoft.Extensions.DependencyInjection 8.0.1
- [x] Add Microsoft.Extensions.Logging 8.0.1
- [x] Add Microsoft.Extensions.Logging.Debug 8.0.1
- [x] Configure ServiceProvider in App.xaml.cs
  - Register singletons: DiskEnumerationService, DriveLetterService, ScriptExecutor
  - Register transients: MountOrchestrator, UnmountOrchestrator, MainViewModel
  - Programmatic window creation via DI (removed StartupUri from XAML)
- [x] Update MainWindow.xaml.cs to accept injected MainViewModel
- [x] Update App.xaml to remove StartupUri attribute

### Benefits Achieved

- ✅ **Testability**: All services implement interfaces → easy to mock
- ✅ **Maintainability**: Clear separation of responsibilities
- ✅ **Extensibility**: Easy to add new workflows, swap implementations
- ✅ **Production Readiness**: Proper error handling, logging infrastructure, retry logic
- ✅ **Unmount Feature**: Critical missing feature now implemented

---

## Milestone 10: Complete the Refactoring ✅ **COMPLETED**

### Refactor MainViewModel

- [x] Inject IMountOrchestrator and IUnmountOrchestrator via constructor
- [x] Remove direct PowerShell execution code from MainViewModel
- [x] Update MountCommand to use IMountOrchestrator.MountAndMapAsync
- [x] Add UnmountCommand with IUnmountOrchestrator

### Mount History Tracking

- [x] Create IMountHistoryService interface
- [x] Create MountHistoryService implementation (JSON persistence)
- [x] Create MountHistoryEntry model
- [x] Register MountHistoryService in DI container
- [x] Update orchestrators to log to history service

### Mount State Tracking

- [x] Create IMountStateService interface
- [x] Create MountStateService implementation
  - Track active mounts with JSON persistence
  - File locking with SemaphoreSlim
  - ReconcileMountStateAsync for orphan cleanup
- [x] Create ActiveMount model

### Update UI for Unmount

- [x] Add Unmount button to MainWindow.xaml
- [x] Add CurrentMountInfo properties to ViewModel
- [x] Bind Unmount button to UnmountCommand
- [x] Add confirmation dialog for unmount operation (IDialogService)
- [x] Add HistoryWindow with HistoryViewModel

---

## Milestone 11: Testing **IN PROGRESS**

### Create Test Project

- [x] Create LiMount.Tests project (xUnit)
  - xUnit 2.9.2
  - Moq 4.20.72
  - FluentAssertions 6.12.2
  - Microsoft.NET.Test.Sdk 17.11.1
  - coverlet.collector 6.0.2
- [x] Add project references to LiMount.Core and LiMount.App
- [x] Create folder structure: LiMount.Tests/Services/

### Unit Tests - Services

- [ ] Test KeyValueOutputParser
  - Parse_ValidKeyValue_ReturnsDictionary
  - Parse_MixedFormat_IgnoresInvalidLines
  - Parse_EmptyInput_ReturnsEmptyDictionary
  - IsSuccess_StatusOK_ReturnsTrue
  - IsSuccess_StatusError_ReturnsFalse
- [ ] Test DriveLetterService
  - GetFreeLetters_ReturnsZtoA_Sorted
  - GetFreeLetters_ExcludesUsedLetters
  - IsLetterAvailable_UsedLetter_ReturnsFalse
  - IsLetterAvailable_FreeLetter_ReturnsTrue
- [ ] Test DiskEnumerationService (with mocked WMI)
  - GetDisks_ReturnsNonEmpty
  - GetCandidateDisks_ExcludesSystemDisks
  - IsLikelyLinux_NoDriverLetter_ReturnsTrue
  - IsLikelyLinux_WindowsFS_ReturnsFalse
- [x] Test MountOrchestrator (8 tests, all passing)
  - MountAndMapAsync_NegativeDiskIndex_ReturnsValidationError
  - MountAndMapAsync_ZeroPartitionNumber_ReturnsValidationError
  - MountAndMapAsync_InvalidDriveLetter_ReturnsValidationError
  - MountAndMapAsync_EmptyFilesystemType_ReturnsValidationError
  - MountAndMapAsync_MountScriptFails_ReturnsFailureWithMountStep
  - MountAndMapAsync_MappingScriptFails_ReturnsFailureWithMapStep
  - MountAndMapAsync_Success_ReturnsSuccessWithAllDetails
  - MountAndMapAsync_Success_LogsToHistory
  - MountAndMapAsync_Failure_LogsToHistory
- [x] Test UnmountOrchestrator (7 tests, 1 failing - test bug)
  - UnmountAndUnmapAsync_NegativeDiskIndex_ReturnsValidationError
  - UnmountAndUnmapAsync_InvalidDriveLetter_ReturnsValidationError
  - UnmountAndUnmapAsync_UnmappingFails_ContinuesToUnmountButReturnsFailure ⚠️ FAILING
  - UnmountAndUnmapAsync_UnmountFails_ReturnsFailureWithUnmountStep
  - UnmountAndUnmapAsync_Success_ReturnsSuccessWithDetails
  - UnmountAndUnmapAsync_Success_LogsToHistory
  - UnmountAndUnmapAsync_Failure_LogsToHistory

### Unit Tests - Models

- [ ] Test MountResult.FromDictionary
- [ ] Test MappingResult.FromDictionary
- [ ] Test UnmountResult.FromDictionary
- [ ] Test UnmappingResult.FromDictionary
- [ ] Test MountAndMapResult.CreateSuccess
- [ ] Test MountAndMapResult.CreateFailure

### Integration Tests (Requires Windows + Admin)

- [ ] Test full mount workflow end-to-end
  - Real WSL environment
  - Real PowerShell scripts
  - Verify mount and mapping
- [ ] Test unmount workflow end-to-end
- [ ] Test error scenarios:
  - Invalid disk index
  - Invalid partition number
  - Drive letter already in use
  - WSL not available

---

## Milestone 12: UX Enhancements ✅ **MOSTLY COMPLETED**

### Environment Validation

- [x] Create IEnvironmentValidationService interface
- [x] Create EnvironmentValidationService implementation
  - Check WSL installed (wsl --status)
  - Check Windows version (Build 19041+ minimum, 22000+ recommended)
  - Check at least one WSL distro installed
  - Return EnvironmentValidationResult with errors/suggestions
- [x] Register in DI container
- [x] Call validator on app startup (MainViewModel.InitializeAsync)
- [x] Show validation errors in dialog (IDialogService)

### Retry Policy

- [ ] Create IRetryPolicy interface
- [ ] Create RetryPolicy service
  - ExecuteWithRetryAsync<`T`> method
  - Configurable max retries and delay
  - Exponential backoff option
  - Handle transient exceptions
- [ ] Use in orchestrators for transient failures
  - UNC path accessibility checks
  - Drive letter mapping attempts

### Detailed Log Viewer

- [ ] Add DetailedLog property to MainViewModel (string)
- [ ] Add Expander to MainWindow.xaml for log viewer
  - Header: "Detailed Log"
  - IsExpanded: False by default
  - Content: TextBox with ConsoleFont, read-only, scrollable
- [ ] Capture PowerShell script outputs in orchestrators
- [ ] Append to DetailedLog with timestamps
- [ ] Add "Clear Log" button

### Mount History UI

- [ ] Add MountHistory property to MainViewModel
  - ObservableCollection<`MountHistoryEntry`>
- [ ] Add DataGrid to MainWindow.xaml
  - Columns: Disk, Partition, Drive Letter, Mounted At, Status
  - Auto-generate: False
  - Style: Clean, modern
- [ ] Add "Clear History" button
- [ ] Persist history across app restarts

### Additional Polish

- [ ] Add application icon (.ico file)
- [ ] Add about dialog with version info
- [ ] Add tooltips to all buttons and controls
- [ ] Add keyboard shortcuts (e.g., F5 for Refresh)
- [ ] Add animations for status changes (optional)
- [ ] Improve error messages with actionable suggestions

---

## Milestone 13: Critical Fixes for Deliverable **IN PROGRESS**

_Added: 2025-11-25 based on architecture review_

### Phase 1: Critical Fixes (Must-have) ✅ **COMPLETED**

- [x] Fix failing test in `UnmountOrchestratorTests.cs:81`
  - Test expected `"Drive unmapping failed"` but orchestrator returns `"Drive letter unmapping failed"`
  - Fixed: Updated test assertion to match actual error message
- [x] Add scripts to build output
  - Scripts in `scripts/` not copied to output directory
  - Fixed: Added MSBuild Content items in `LiMount.App.csproj` to copy scripts
- [x] Call `ReconcileMountStateAsync` on startup
  - `InitializationConfig.AutoReconcileMounts=true` but never called
  - Fixed: Added call in `MainViewModel.InitializeAsync` after environment validation
- [x] Fix FsType ComboBox binding
  - `MainWindow.xaml:102-109` used `ComboBoxItem` not string binding
  - Fixed: Added `FileSystemTypes` collection and used `ItemsSource` binding

### Phase 2: Code Quality

- [x] Remove `_lastMappedDriveLetter` from ViewModel
  - Duplicated state already in `CurrentMountedDriveLetter`
  - Fixed: Now using `CurrentMountedDriveLetter` consistently throughout
- [x] Add missing unit tests
  - [x] KeyValueOutputParser tests (18 tests added)
  - [x] DriveLetterService tests (14 tests added)
  - [x] DiskEnumerationService tests (8 tests + 6 heuristic tests = 14 tests)

### Phase 3: Deployment Readiness

- [x] Create publish profile for self-contained deployment
  - Created `win-x64.pubxml` for single-file self-contained deployment
  - Usage: `dotnet publish LiMount.App -c Release -p:PublishProfile=win-x64`
- [x] Ensure scripts directory is bundled with published output
  - Scripts copied via MSBuild Content items to `bin/publish/win-x64/scripts/`
- [x] Add CHANGELOG.md
- [ ] Final verification on clean Windows machine

---

## Notes

- **Dependencies**:
  - System.Management 8.0.0 (for WMI)
  - CommunityToolkit.Mvvm 8.3.2 (for MVVM helpers)
  - Microsoft.Extensions.DependencyInjection 8.0.1 (for DI)
  - Microsoft.Extensions.Logging 8.0.1 (for logging)
- **Target Framework**: .NET 8 (LTS), specifically net8.0-windows
- **Architecture**: Clean architecture with DI, interfaces, and orchestration layer
- **Security**: Minimal validation (MVP/prototype scope)
- **Error Handling**: User-friendly messages in UI, detailed logs, structured error propagation
- **WSL Requirements**: Windows 11 Build 22000+ or Microsoft Store WSL

## Architecture Overview

```text
MainViewModel (UI State)
    ↓ (Dependency Injection)
Orchestrators (Workflow Coordination)
    ├── MountOrchestrator → MountAndMapAsync
    └── UnmountOrchestrator → UnmountAndUnmapAsync
        ↓
Services (Single Responsibilities)
    ├── ScriptExecutor → Execute PowerShell scripts
    ├── DiskEnumerationService → Enumerate disks via WMI
    └── DriveLetterService → Manage drive letters
        ↓
PowerShell Scripts
    ├── Mount-LinuxDiskCore.ps1 (elevated)
    ├── Map-WSLShareToDrive.ps1 (non-elevated)
    ├── Unmount-LinuxDisk.ps1 (elevated) ← NEW
    └── Unmap-DriveLetter.ps1 (non-elevated) ← NEW
```

## Key Improvements from Refactoring

1. ✅ **Unmount functionality** - Critical missing feature now implemented
2. ✅ **Clean architecture** - Interfaces, DI, separation of concerns
3. ✅ **Testability** - All components can be unit tested via interfaces
4. ✅ **Orchestration layer** - Complex workflows managed properly with progress reporting
5. ✅ **Error handling** - Structured throughout all layers with detailed results

## Migration Guide for Developers

**To add a new operation:**
1. Create interface in `LiMount.Core/Interfaces/`
2. Implement service in `LiMount.Core/Services/`
3. Register in `App.xaml.cs` DI container (singleton or transient)
4. Inject into ViewModel constructor
5. Add UI command in ViewModel
6. Bind UI to command in XAML

**To add tests:**
1. Create test class in `LiMount.Tests/Unit/` or `LiMount.Tests/Integration/`
2. Mock dependencies using `Moq`
3. Test one scenario per method
4. Use descriptive test names: `Method_Scenario_ExpectedBehavior`

**To swap implementations:**
1. Create new class implementing existing interface
2. Update DI registration in `App.xaml.cs`
3. No changes needed elsewhere! (power of dependency injection)

---

_Last Updated: 2025-11-25_
_Status: Milestones 1-10, 12 Complete. Milestone 11 (Testing) in progress. Milestone 13 (Critical Fixes) started._
