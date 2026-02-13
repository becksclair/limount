# LiMount

**LiMount** is a Windows desktop application that mounts Linux disk partitions into WSL2 and exposes them in Windows Explorer.  
Windows access is now **mode-based**:
- `NetworkLocation` (default): creates an Explorer Network Location under NetHood.
- `DriveLetterLegacy`: maps UNC to a Windows drive letter.
- `None`: mount-only, with no Windows integration.

Built with C# WinUI 3 (.NET 10) and PowerShell.

## Features

- **Disk Enumeration**: Detects physical disks and partitions using WMI
- **Smart Filtering**: Prioritizes likely Linux partitions (ext4, xfs, btrfs, etc.)
- **Safety Checks**: Blocks system/boot disks from mount workflows
- **WSL2 Integration**: Uses `wsl --mount` for Linux filesystem mounting
- **Windows Access Modes**:
  - `NetworkLocation` (default)
  - `DriveLetterLegacy` (explicit legacy mode)
  - `None` (no Windows-side access surface)
- **Explorer Integration**: Opens mounted paths directly in Explorer (mode-aware)
- **Deterministic UI Tests**: FlaUI automation with optional screenshot artifact capture

## Requirements

### System Requirements

- **Operating System**: Windows 10 (Build 19041+) or Windows 11 (Build 22000+ recommended)
- **WSL**: WSL2 installed and configured
  - Requires `wsl --mount` support (Windows 11 Build 22000+ or Microsoft Store WSL)
- **PowerShell**: Windows PowerShell 5.1 or later
- **Privileges**: Administrator access (for mount/unmount scripts)

### Development Requirements

- **.NET SDK 10.0.101+** (`global.json` pins 10.0.101 with `rollForward: latestPatch`)
- **Visual Studio 2022** or **Visual Studio Code** (optional)

## Project Structure

```text
limount/
├── LiMount.sln
├── LiMount.Core/
│   ├── Interfaces/
│   │   ├── IMountOrchestrator.cs
│   │   ├── IUnmountOrchestrator.cs
│   │   ├── IWindowsAccessService.cs
│   │   └── ...
│   ├── Models/
│   │   ├── ActiveMount.cs
│   │   ├── MountAndMapResult.cs
│   │   ├── UnmountAndUnmapResult.cs
│   │   ├── MountHistoryEntry.cs
│   │   ├── WindowsAccessRequest.cs
│   │   ├── WindowsAccessInfo.cs
│   │   └── ...
│   └── Services/
│       ├── MountOrchestrator.cs
│       ├── UnmountOrchestrator.cs
│       ├── MountStateService.cs
│       ├── Access/WindowsAccessService.cs
│       └── ...
├── LiMount.WinUI/
│   ├── ViewModels/
│   ├── Views/
│   ├── TestMode/
│   └── App.xaml.cs
├── LiMount.Tests/
├── LiMount.UITests/
└── scripts/
    ├── Mount-LinuxDiskCore.ps1
    ├── Unmount-LinuxDisk.ps1
    ├── Map-WSLShareToDrive.ps1
    ├── Unmap-DriveLetter.ps1
    └── network/
        ├── Create-NetworkLocation.ps1
        └── Remove-NetworkLocation.ps1
```

## Build and Run

```powershell
dotnet restore
dotnet build --configuration Release
dotnet run --project LiMount.WinUI
```

## Automated Testing

### Unit and service tests

```powershell
dotnet test LiMount.Tests
```

### Deterministic UI automation (FlaUI + test mode)

```powershell
$env:LIMOUNT_RUN_UI_TESTS='1'
dotnet test LiMount.UITests
Remove-Item Env:\LIMOUNT_RUN_UI_TESTS
```

Optional screenshot capture:

```powershell
$env:LIMOUNT_RUN_UI_TESTS='1'
$env:LIMOUNT_CAPTURE_SCREENSHOT='1'
dotnet test LiMount.UITests
Remove-Item Env:\LIMOUNT_RUN_UI_TESTS
Remove-Item Env:\LIMOUNT_CAPTURE_SCREENSHOT
```

Full deterministic screenshot batch:

```powershell
$env:LIMOUNT_RUN_UI_TESTS='1'
$env:LIMOUNT_CAPTURE_SCREENSHOT_BATCH='1'
dotnet test LiMount.UITests --filter "FullyQualifiedName~ScreenshotBatchUiTests"
Remove-Item Env:\LIMOUNT_RUN_UI_TESTS
Remove-Item Env:\LIMOUNT_CAPTURE_SCREENSHOT_BATCH
```

Batch output:
- `screenshots\ui-batch\<yyyyMMdd-HHmmss>\`
- Deterministic files `01-main-initial.png` through `12-main-after-wizard.png`

### Hardware-in-loop mount regression (real WSL + disk)

Expected-failure validation (unsupported XFS feature cases):

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\run-hil-mount-test.ps1 -DiskIndex 1 -Partition 2 -ExpectXfsUnsupported
```

Drive-level end-to-end validation (failure partition + success partition):

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\run-hil-mount-test.ps1 -DiskIndex 1 -VerifyDriveEndToEnd -FailurePartition 2
```

## Usage

### Basic Workflow

1. Launch LiMount and refresh disks.
2. Select a non-system disk and Linux partition.
3. Select filesystem type.
4. Choose Windows access mode:
   - `NetworkLocation` (default, no drive letter required)
   - `DriveLetterLegacy` (requires drive letter)
   - `None` (mount-only)
5. Click **Mount** and approve elevation.
6. Use **Open in Explorer**:
   - `NetworkLocation`: opens UNC/network location target
   - `DriveLetterLegacy`: opens `<DriveLetter>:\`
   - `None`: Explorer action is disabled
7. Click **Unmount** to clean up WSL and Windows access artifacts.

## PowerShell Script Contracts

### Mount-LinuxDiskCore.ps1 (Elevated)

Mounts a physical disk partition into WSL2 (`wsl --mount`) and returns key-value output including:
- `STATUS`
- `DistroName`
- `MountPathLinux`
- `MountPathUNC`
- `AlreadyMounted`
- `UncVerified`
- Optional diagnostics (`ErrorCode`, `ErrorHint`, `DmesgSummary`)

### Create-NetworkLocation.ps1 (Non-Elevated)

Creates an Explorer Network Location under:
- `%AppData%\Microsoft\Windows\Network Shortcuts\<Name>\`

Output keys:
- `STATUS`
- `NetworkLocationName`
- `NetworkLocationPath`
- `TargetUNC`

### Remove-NetworkLocation.ps1 (Non-Elevated)

Removes the Network Location folder created under NetHood.

Output keys:
- `STATUS`
- `NetworkLocationName`

### Map-WSLShareToDrive.ps1 / Unmap-DriveLetter.ps1 (Non-Elevated, legacy mode)

Used only when `DriveLetterLegacy` is selected.

## Architecture Notes

- **Orchestrator contracts are access-mode-first** (`IMountOrchestrator`, `IUnmountOrchestrator`).
- **`IWindowsAccessService`** routes Windows integration by mode:
  - `NetworkLocation` -> NetHood scripts
  - `DriveLetterLegacy` -> drive mapping scripts
  - `None` -> no-op
- **State and history are mode-aware**:
  - `ActiveMount` tracks `AccessMode`, optional `DriveLetter`, optional `NetworkLocationName`
  - History/result models persist access metadata for restore and diagnostics
- **Mount state reconciliation is mode-specific**:
  - Drive-letter pruning applies to `DriveLetterLegacy` only
  - `NetworkLocation`/`None` entries are not pruned for missing drive letters

## Known Limitations

- **Single active mount UX**: state can track multiple records, but main UI still presents a single active mount workflow.
- **Windows 11 preferred**: `wsl --mount` reliability/features are best on modern builds.
- **Kernel compatibility limits**: some XFS/ext feature sets may still be unsupported by the installed WSL kernel.
- **No VM fallback yet**: unsupported filesystem fallback to a Linux VM remains planned work.

## Troubleshooting

### "`wsl --mount` is not supported"

- Use Windows 11 Build 22000+ or Microsoft Store WSL.
- Run `wsl --update`.

### XFS mount fails with `Invalid argument`

Check WSL kernel logs:

```powershell
wsl -e sh -lc "dmesg | tail -n 200"
```

If you see unknown incompatible filesystem features, this is a kernel/filesystem compatibility issue.

### Network Location not visible immediately

- Refresh Explorer (`F5`).
- Confirm NetHood path exists:
  - `%AppData%\Microsoft\Windows\Network Shortcuts`
- Verify UNC is reachable:
  - `Test-Path \\wsl.localhost\<Distro>\mnt\wsl\PHYSICALDRIVEXpY`

### Legacy drive letter issues

- Legacy mode only: verify `subst` mappings:
  - `subst`
- Choose another free drive letter if conflict exists.

### None mode cannot open Explorer

Expected behavior: `None` intentionally performs no Windows integration surface creation.

## References

- [WSL2 Disk Mounting Documentation](https://learn.microsoft.com/en-us/windows/wsl/wsl2-mount-disk)
- [WinUI 3 Documentation](https://learn.microsoft.com/en-us/windows/apps/winui/winui3/)
- [Windows Network Shortcuts (NetHood) behavior](https://superuser.com/questions/1781239/how-can-i-create-a-shortcut-in-this-pc-on-windows-10)

---

Built for the WSL community.
