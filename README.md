# LiMount

**LiMount** is a Windows desktop application that simplifies mounting Linux disk partitions into WSL2 and mapping them as Windows drive letters. Built with C# WPF (.NET 8) and PowerShell, LiMount provides a clean, modern GUI for managing WSL disk mounts.

## Features

- **Disk Enumeration**: Automatically detects physical disks and partitions using WMI
- **Smart Filtering**: Identifies and filters likely Linux partitions (ext4, xfs, btrfs, etc.)
- **Safety Checks**: Prevents mounting system or boot disks
- **WSL2 Integration**: Uses `wsl --mount` to mount Linux partitions
- **Drive Mapping**: Maps WSL UNC paths to Windows drive letters for easy access
- **Modern UI**: Clean, intuitive WPF interface with real-time status updates
- **Explorer Integration**: Opens mounted drives directly in Windows Explorer

## Requirements

### System Requirements

- **Operating System**: Windows 10 (Build 19041+) or Windows 11 (Build 22000+ recommended)
- **WSL**: Windows Subsystem for Linux 2 (WSL2) installed and configured
  - Requires `wsl --mount` support (Windows 11 Build 22000+ or Microsoft Store WSL)
- **PowerShell**: Windows PowerShell 5.1 or later
- **Privileges**: Administrator access (for mounting disks)

### Development Requirements

- **.NET 8 SDK**: [Download .NET 8](https://dotnet.microsoft.com/download/dotnet/8.0)
- **Visual Studio 2022** or **Visual Studio Code** (optional, for development)

## Project Structure

```text
limount/
├── LiMount.sln                      # Solution file
├── LiMount.Core/                    # Core library (.NET 8)
│   ├── Interfaces/                 # Service interfaces
│   │   ├── IDiskEnumerationService.cs
│   │   ├── IDriveLetterService.cs
│   │   ├── IMountOrchestrator.cs
│   │   ├── IScriptExecutor.cs
│   │   └── IUnmountOrchestrator.cs
│   ├── Models/                      # Data models
│   │   ├── DiskInfo.cs
│   │   ├── PartitionInfo.cs
│   │   ├── DriveLetterInfo.cs
│   │   ├── MountResult.cs
│   │   ├── MappingResult.cs
│   │   ├── MountAndMapResult.cs
│   │   ├── UnmountResult.cs
│   │   ├── UnmappingResult.cs
│   │   └── UnmountAndUnmapResult.cs
│   └── Services/                    # Business logic services
│       ├── DiskEnumerationService.cs
│       ├── DriveLetterService.cs
│       ├── KeyValueOutputParser.cs
│       ├── ScriptExecutor.cs
│       ├── MountOrchestrator.cs
│       └── UnmountOrchestrator.cs
├── LiMount.App/                     # WPF application (.NET 8)
│   ├── ViewModels/                  # MVVM ViewModels
│   │   └── MainViewModel.cs
│   ├── Converters/                  # Value converters
│   │   └── InverseBooleanConverter.cs
│   ├── MainWindow.xaml
│   ├── MainWindow.xaml.cs
│   ├── App.xaml
│   └── App.xaml.cs
└── scripts/                         # PowerShell helper scripts
    ├── Mount-LinuxDiskCore.ps1      # Elevated: WSL mount script
    ├── Map-WSLShareToDrive.ps1      # Non-elevated: Drive mapping script
    ├── Unmount-LinuxDisk.ps1        # Elevated: WSL unmount script
    └── Unmap-DriveLetter.ps1        # Non-elevated: Drive unmapping script
```

## Building the Application

### Prerequisites

1. Install [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
2. Ensure WSL2 is installed and configured on your Windows system

### Build Steps

1. **Clone or download this repository**
   ```bash
   git clone <repository-url>
   cd limount
   ```

2. **Restore NuGet packages**
   ```bash
   dotnet restore
   ```

3. **Build the solution**
   ```bash
   dotnet build --configuration Release
   ```

4. **Run the application**
   ```bash
   dotnet run --project LiMount.App
   ```

   Or navigate to the build output:
   ```bash
   cd LiMount.App/bin/Release/net8.0-windows
   ./LiMount.App.exe
   ```

### Publish Matrix

| UI stack | Mode        | Command                                                                                                  | Output                                                  |
|----------|-------------|-----------------------------------------------------------------------------------------------------------|---------------------------------------------------------|
| WPF      | ReadyToRun  | `.\scripts\publish-wpf-r2r.ps1`                                                                          | `LiMount.App/bin/publish/win-x64/`                     |
| WinUI 3  | NativeAOT   | `.\scripts\publish-winui-aot.ps1` (requires .NET SDK 8.0.402+, WinSDK/MSVC already installed)            | `LiMount.WinUI/bin/Release/net8.0-windows10.0.22621.0/win-x64/publish/` |

Known warnings on WinUI AOT: trim/AOT analysis from Serilog and WebView2 (transitive via WinAppSDK); build still succeeds.

## Usage

### Basic Workflow

1. **Launch LiMount**
   - Run `LiMount.App.exe`
   - The application will enumerate available disks and partitions

2. **Select Disk**
   - Choose a physical disk from the dropdown
   - Only non-system, non-boot disks are shown for safety

3. **Select Partition**
   - Choose a partition (filtered to show likely Linux partitions)
   - Partitions without Windows drive letters are prioritized

4. **Select Drive Letter**
   - Choose an available drive letter (sorted Z→A by default)

5. **Select Filesystem Type**
   - Choose the filesystem type (ext4, xfs, btrfs, vfat)
   - Default: ext4

6. **Mount**
   - Click the **Mount** button
   - You'll be prompted for Administrator elevation (UAC)
   - The app will:
     1. Mount the disk partition in WSL2
     2. Detect the mount path (e.g., `/mnt/wsl/PHYSICALDRIVE2p1`)
     3. Map the WSL UNC path to your selected drive letter
   - Status updates will appear in the Status section

7. **Access Your Drive**
   - Click **Open in Explorer** to browse the mounted drive
   - Or access it directly via `<DriveLetter>:\` in File Explorer

## PowerShell Scripts

### Mount-LinuxDiskCore.ps1 (Elevated)

**Purpose**: Mounts a physical disk partition into WSL2 using `wsl --mount`.

**Parameters**:
- `DiskIndex` (required): Physical disk index (e.g., 2 for `\\.\PHYSICALDRIVE2`)
- `Partition` (required): Partition number (1-based)
- `FsType` (optional): Filesystem type (ext4, xfs, btrfs, vfat). Default: ext4
- `DistroName` (optional): Specific WSL distribution name

**Output**: Machine-readable key=value pairs:
```text
STATUS=OK
DistroName=Ubuntu
MountPathLinux=/mnt/wsl/PHYSICALDRIVE2p1
MountPathUNC=\\wsl$\Ubuntu\mnt\wsl\PHYSICALDRIVE2p1
```

**Example**:
```powershell
.\Mount-LinuxDiskCore.ps1 -DiskIndex 2 -Partition 1 -FsType ext4
```

### Map-WSLShareToDrive.ps1 (Non-Elevated)

**Purpose**: Maps a WSL UNC path to a Windows drive letter.

**Parameters**:
- `DriveLetter` (required): Drive letter to map (e.g., "L" or "L:")
- `TargetUNC` (required): UNC path to map (e.g., `\\wsl$\Ubuntu\mnt\wsl\PHYSICALDRIVE2p1`)

**Output**: Machine-readable key=value pairs:
```text
STATUS=OK
DriveLetter=L
MappedTo=\\wsl$\Ubuntu\mnt\wsl\PHYSICALDRIVE2p1
```

**Example**:
```powershell
.\Map-WSLShareToDrive.ps1 -DriveLetter L -TargetUNC "\\wsl$\Ubuntu\mnt\wsl\PHYSICALDRIVE2p1"
```

## Architecture

### Technologies Used

- **C# / .NET 8**: Core language and runtime (LTS)
  - **Target Framework**: `net8.0-windows` (Windows-specific)
  - Both LiMount.Core and LiMount.App target Windows-only APIs
- **WPF (Windows Presentation Foundation)**: UI framework
- **CommunityToolkit.Mvvm**: MVVM helpers (RelayCommand, ObservableProperty)
- **System.Management**: WMI access for disk enumeration (Windows-only)
- **PowerShell**: Helper scripts for WSL mounting and drive mapping
- **Platform Attributes**: `[SupportedOSPlatform("windows")]` for Windows-specific services

### Design Patterns

- **MVVM (Model-View-ViewModel)**: Separation of UI and business logic
- **Service Layer**: Encapsulated business logic in `LiMount.Core.Services`
- **Key-Value Parsing**: Machine-readable output from PowerShell scripts

### Key Components

1. **DiskEnumerationService** (Windows-only): Uses WMI (`Win32_DiskDrive`, `Win32_DiskPartition`, `Win32_LogicalDisk`) to enumerate disks and partitions, with heuristics to identify Linux filesystems. Marked with `[SupportedOSPlatform("windows")]` attribute.

2. **DriveLetterService** (Windows-only): Enumerates used and free Windows drive letters using `DriveInfo.GetDrives()`. Manages A-Z drive letter mappings specific to Windows.

3. **KeyValueOutputParser**: Parses `key=value` output from PowerShell scripts into dictionaries

4. **MainViewModel**: Orchestrates the entire mount workflow, including:
   - Disk and partition selection
   - Elevated PowerShell script execution
   - Drive letter mapping
   - Status updates and error handling

## Known Limitations

This is an MVP/prototype with the following limitations:

- **Minimal Error Recovery**: Error handling is basic; failed mounts may require manual cleanup
- **Limited Unmount Feature**: Basic unmount functionality available but may require manual cleanup in some cases
- **Single Distro Support**: Auto-detects the first WSL distro; multi-distro scenarios may need manual specification
- **Windows 11 Preferred**: `wsl --mount` works best on Windows 11 Build 22000+; older builds may have limited support
- **No Persistence**: Drive mappings are session-based and may not survive reboots
- **Limited Testing**: Tested primarily with ext4; other filesystems may behave differently

## Troubleshooting

### "wsl --mount is not supported"

- Ensure you're running Windows 11 Build 22000+ or have the Microsoft Store version of WSL installed
- Update WSL: `wsl --update`

### "Disk X is a system or boot disk"

- LiMount refuses to mount system/boot disks for safety
- Only select secondary/external disks

### "UNC path is not reachable"

- Ensure WSL2 is running: `wsl --list --verbose`
- Verify the mount succeeded: `wsl -- ls /mnt/wsl`
- Check network sharing is enabled for WSL

### Drive letter not appearing in Explorer

- Refresh Explorer (F5)
- Try a different drive letter
- Verify the mapping: `net use` in Command Prompt

## Contributing

This is a prototype/MVP project. Contributions are welcome! Areas for improvement:

- **Enhanced Unmount Feature**: Improve UI and reliability for unmount and unmap operations
- **Multi-Distro Support**: Allow selecting which WSL distro to use
- **Persistent Mappings**: Option to persist drive mappings across reboots
- **Better Error Handling**: More robust error recovery and user guidance
- **Unit Tests**: Expand test coverage for services and utilities
- **Logging**: Add detailed logging for troubleshooting

## License

[Specify your license here, e.g., MIT, Apache 2.0, etc.]

## Acknowledgments

- **Microsoft WSL Team**: For `wsl --mount` functionality
- **CommunityToolkit.Mvvm**: For excellent MVVM helpers
- **.NET Team**: For .NET 8 and WPF

## References

- [WSL2 Disk Mounting Documentation](https://learn.microsoft.com/en-us/windows/wsl/wsl2-mount-disk)
- [.NET 8 WPF Documentation](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/whats-new/net80)
- [Win32_DiskDrive WMI Class](https://learn.microsoft.com/en-us/windows/win32/cimwin32prov/win32-diskdrive)
- [ProcessStartInfo Documentation](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.processstartinfo)

---

**Built with ❤️ for the WSL community**
