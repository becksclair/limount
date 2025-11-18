# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

**LiMount** is a Windows desktop application that mounts Linux disk partitions into WSL2 and maps them as Windows drive letters. Built with C# WPF (.NET 8) and PowerShell scripts.

**Key Technologies:**
- .NET 8 (LTS) with `net8.0-windows` target framework
- WPF (Windows Presentation Foundation)
- CommunityToolkit.Mvvm (v8.3.2) for MVVM helpers
- System.Management (v10.0.0) for WMI disk enumeration
- Serilog for file logging
- PowerShell for elevated WSL operations

**Platform:** Windows-only. Services use `[SupportedOSPlatform("windows")]` attributes.

## Building and Running

### Build Commands

```bash
# Restore dependencies
dotnet restore

# Build entire solution
dotnet build --configuration Debug
dotnet build --configuration Release

# Run the WPF application
dotnet run --project LiMount.App

# Build a specific project
dotnet build LiMount.Core
dotnet build LiMount.App
```

### Development Notes

- No automated tests currently exist in the project
- Application requires Administrator elevation for mounting operations
- The application creates logs at: `%LocalAppData%\LiMount\logs\limount-*.log` (production mode only)
- PowerShell scripts are located in `scripts/` directory and must be accessible at runtime

## Project Structure

```
LiMount.sln              # Solution file with 2 projects
├── LiMount.Core/        # Core library (Windows-only .NET 8)
│   ├── Interfaces/      # Service contracts
│   ├── Models/          # Data transfer objects and results
│   └── Services/        # Business logic and WMI/PowerShell interactions
├── LiMount.App/         # WPF application (Windows-only .NET 8)
│   ├── ViewModels/      # MVVM ViewModels
│   ├── Converters/      # WPF value converters
│   ├── MainWindow.xaml  # Main UI
│   └── App.xaml.cs      # DI configuration and startup
└── scripts/             # PowerShell helper scripts
    ├── Mount-LinuxDiskCore.ps1    # Elevated: WSL mount
    ├── Map-WSLShareToDrive.ps1    # Non-elevated: Drive mapping
    ├── Unmount-LinuxDisk.ps1      # Elevated: WSL unmount
    └── Unmap-DriveLetter.ps1      # Non-elevated: Drive unmapping
```

## Architecture

### Layered Design

**LiMount.Core** (Business Logic):
- **Services Layer**: Platform-specific Windows services that interact with WMI and PowerShell
  - `DiskEnumerationService`: Uses WMI (`Win32_DiskDrive`, `Win32_DiskPartition`, `Win32_LogicalDisk`) to enumerate disks and partitions
  - `DriveLetterService`: Manages Windows drive letters using `DriveInfo.GetDrives()`
  - `ScriptExecutor`: Executes PowerShell scripts with proper elevation handling
  - `MountOrchestrator`: Orchestrates mount + mapping workflow with retry logic
  - `UnmountOrchestrator`: Orchestrates unmount + unmapping workflow
  - `KeyValueOutputParser`: Parses `key=value` output from PowerShell scripts

**LiMount.App** (Presentation):
- **MVVM Pattern**: Uses CommunityToolkit.Mvvm source generators
  - `MainViewModel`: Main application logic with `[ObservableProperty]` and `[RelayCommand]` attributes
  - Async initialization pattern to keep UI responsive during disk enumeration
  - Progress reporting via `IProgress<string>` interface

### Dependency Injection

Configured in `App.xaml.cs` using `Microsoft.Extensions.DependencyInjection`:

```csharp
// Core services (Singleton)
services.AddSingleton<IDiskEnumerationService, DiskEnumerationService>();
services.AddSingleton<IDriveLetterService, DriveLetterService>();
services.AddSingleton<IScriptExecutor, ScriptExecutor>();

// Orchestrators (Transient)
services.AddTransient<IMountOrchestrator, MountOrchestrator>();
services.AddTransient<IUnmountOrchestrator, UnmountOrchestrator>();

// ViewModels and Windows (Transient)
services.AddTransient<MainViewModel>();
services.AddTransient<MainWindow>();
```

### PowerShell Script Execution

**Two execution modes:**

1. **Elevated (Mount/Unmount)**: Uses `ProcessStartInfo` with `Verb = "runas"` and `UseShellExecute = true`
   - Cannot redirect stdout/stderr, so scripts write to temp files
   - Temp file pattern: `%TEMP%\limount_mount_{diskIndex}_{partition}.txt` or `limount_unmount_{diskIndex}.txt`
   - ScriptExecutor polls for temp file with timeout

2. **Non-Elevated (Mapping/Unmapping)**: Uses `UseShellExecute = false` with redirected stdout/stderr
   - Direct capture of script output

**Script Output Format**: All scripts use machine-readable `key=value` format:
```
STATUS=OK
DistroName=Ubuntu
MountPathLinux=/mnt/wsl/PHYSICALDRIVE2p1
MountPathUNC=\\wsl$\Ubuntu\mnt\wsl\PHYSICALDRIVE2p1
```

### Key Components

**DiskEnumerationService**:
- Queries WMI for disk and partition information
- Implements heuristics to identify Linux partitions (no drive letter, non-NTFS/FAT32)
- Filters out system/boot disks for safety
- Returns `DiskInfo` objects with nested `PartitionInfo` collections

**ScriptExecutor**:
- Auto-locates `scripts/` directory using relative path search from application base directory
- Handles both elevated and non-elevated PowerShell execution
- Parses script output using `KeyValueOutputParser`
- Temp file cleanup after elevated operations

**MountOrchestrator**:
- Two-phase operation: (1) WSL mount, (2) drive mapping
- Includes UNC path accessibility verification with retry (5 attempts, 500ms delay)
- Progress reporting throughout the workflow
- Returns `MountAndMapResult` with detailed success/failure information

**MainViewModel**:
- Async initialization pattern: `InitializeAsync()` called from `MainWindow.OnLoaded`
- Disk enumeration runs on background thread, UI updates on dispatcher
- Auto-selection of first available disk, partition, and drive letter
- Command execution guarded by `CanMount()` and `CanOpenExplorer()` methods

## Coding Patterns

### MVVM with Source Generators

Use CommunityToolkit.Mvvm source generators for properties and commands:

```csharp
[ObservableProperty]
private bool _isBusy;  // Generates IsBusy property with INotifyPropertyChanged

[RelayCommand(CanExecute = nameof(CanMount))]
private async Task MountAsync() { }  // Generates MountCommand
```

### Result Objects

All operations return strongly-typed result objects with `Success` flag:
- `MountResult`: WSL mount operation results
- `MappingResult`: Drive mapping results
- `MountAndMapResult`: Combined operation results
- `UnmountResult`: WSL unmount results
- `UnmappingResult`: Drive unmapping results

Each result class has `FromDictionary()` static method for parsing script output.

### Async Patterns

- Services use `async/await` throughout
- UI operations check thread using `Application.Current.Dispatcher.InvokeAsync()`
- `IProgress<string>` for progress reporting from orchestrators to ViewModel
- `CancellationToken` support in initialization

## Common Tasks

### Adding a New Service

1. Define interface in `LiMount.Core/Interfaces/I{ServiceName}.cs`
2. Implement service in `LiMount.Core/Services/{ServiceName}.cs`
3. Add `[SupportedOSPlatform("windows")]` attribute if using Windows-specific APIs
4. Register in `App.xaml.cs` DI configuration
5. Inject into ViewModels or orchestrators via constructor

### Adding a New PowerShell Script

1. Create script in `scripts/` directory with proper parameter validation
2. Implement machine-readable output using `key=value` format
3. Add execution method in `ScriptExecutor` (elevated or non-elevated)
4. Create corresponding result model in `LiMount.Core/Models/`
5. Add `FromDictionary()` method to parse script output

### Modifying the UI

1. Update XAML in `MainWindow.xaml`
2. Add properties/commands to `MainViewModel.cs` using `[ObservableProperty]` and `[RelayCommand]`
3. Create value converters in `LiMount.App/Converters/` if needed for binding transformations
4. Wire up bindings in XAML

## Important Constraints

### Safety Mechanisms

- **System/Boot Disk Protection**: `DiskEnumerationService.GetCandidateDisks()` filters out disks marked as system or boot disks
- **Elevated Operations**: Mount/unmount operations require Administrator privileges (UAC prompt)
- **Filesystem Validation**: Mount script validates filesystem type against allowed list (ext4, xfs, btrfs, vfat)

### Known Limitations

- No automated unmount on application exit
- Drive mappings are session-based (not persistent across reboots)
- Single WSL distro auto-detection (first available distro)
- Minimal error recovery for failed operations
- No unit tests currently exist

### Script Path Resolution

`ScriptExecutor` searches for `scripts/` directory in multiple locations:
1. `{AppDirectory}/scripts`
2. `{AppDirectory}/../../../scripts` (for development builds)
3. `{AppDirectory}/../../../../scripts`
4. `{AppDirectory}/../../../../../scripts`

When adding or modifying scripts, ensure they remain in the root `scripts/` directory.

## Logging

Application uses Serilog with conditional file logging:
- **Debug Mode** (debugger attached): Logs to Debug output only
- **Production Mode**: Logs to `%LocalAppData%\LiMount\logs\limount-{Date}.log`
  - Rolling daily logs, retains last 7 days
  - 10MB file size limit with rollover
  - Configured in `App.xaml.cs` startup

To enable production logging in debug mode, set environment variable: `DOTNET_ENVIRONMENT=Production`

## WSL Integration

### Mount Operation Flow

1. `wsl --mount \\.\PHYSICALDRIVE{Index} --partition {Partition} --type {FsType}`
2. Detect mount path by querying `/mnt/wsl/` directory in WSL
3. Build UNC path: `\\wsl$\{DistroName}{MountPathLinux}`
4. Verify UNC accessibility with retry logic
5. Map UNC to drive letter using `net use` or similar

### Unmount Operation Flow

1. Remove drive letter mapping using Windows API
2. `wsl --unmount \\.\PHYSICALDRIVE{Index}`

### Requirements

- Windows 11 Build 22000+ or Microsoft Store WSL version
- WSL2 must be running (at least one distro)
- Administrator privileges for mount/unmount operations
