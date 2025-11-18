# LiMount - TODO List

**Project**: Windows desktop GUI for mounting Linux disks/partitions into WSL2 and mapping them as Windows drive letters.

**Tech Stack**: C# WPF (.NET 8), PowerShell helpers

---

## Research & Documentation

- [x] Research latest .NET 8 and WPF documentation
  - .NET 8 is LTS (3-year support)
  - WPF has hardware acceleration for RDP and OpenFolderDialog
  - Docs: https://learn.microsoft.com/en-us/dotnet/desktop/wpf/whats-new/net80
- [x] Research System.Management and WMI for disk enumeration
  - Win32_DiskDrive class for physical disks
  - Win32_Volume for partitions and volumes
  - Docs: https://learn.microsoft.com/en-us/windows/win32/cimwin32prov/win32-diskdrive
- [x] Research WSL2 `wsl --mount` command
  - Requires Windows 11 Build 22000+ or Microsoft Store WSL
  - Requires Administrator access
  - Syntax: `wsl --mount <Disk> --partition <Index> --type <FsType>`
  - Docs: https://learn.microsoft.com/en-us/windows/wsl/wsl2-mount-disk
- [x] Research ProcessStartInfo for PowerShell execution
  - Use UseShellExecute=false for stream redirection
  - RedirectStandardOutput/Error for capturing output
  - Verb="runas" for elevation
  - Docs: https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.processstartinfo

---

## Milestone 1: Solution Skeleton

- [x] Initialize .NET solution (LiMount.sln)
- [x] Create LiMount.Core class library (.NET 8)
- [x] Create LiMount.App WPF project (.NET 8)
- [x] Add project reference: LiMount.App → LiMount.Core
- [x] Create scripts folder at repo root
- [ ] Verify solution builds with `dotnet build`

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
  - Determine mount path: ls /mnt/wsl, find PHYSICALDRIVE<DiskIndex>
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
    - ObservableCollection<DiskInfo> Disks
    - DiskInfo? SelectedDisk
    - ObservableCollection<PartitionInfo> Partitions (filtered by SelectedDisk)
    - PartitionInfo? SelectedPartition
    - ObservableCollection<char> FreeDriveLetters
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

## Milestone 7: Integration & Testing

- [x] Wire up MainWindow.xaml to MainViewModel
- [x] Add System.Management package to LiMount.Core (verify .NET 8 compatibility)
- [ ] Build entire solution: `dotnet build` (requires Windows with .NET 8 SDK)
- [ ] Fix any build errors (if any)
- [ ] Create LiMount.Tests project (optional)
  - Unit test: IsLikelyLinux heuristic
  - Unit test: Free drive letters sorted Z→A
  - Unit test: KeyValueOutputParser with sample inputs
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
- [ ] Add logging/diagnostics (optional)
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

## Notes

- **Dependencies**: System.Management (for WMI), possibly CommunityToolkit.Mvvm (for MVVM helpers)
- **Target Framework**: .NET 8 (LTS)
- **Security**: Minimal validation (MVP/prototype scope)
- **Error Handling**: User-friendly messages in UI, detailed logs in console/file
- **WSL Requirements**: Windows 11 Build 22000+ or Microsoft Store WSL

---

_Last Updated: 2025-11-18_
