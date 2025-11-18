# LiMount – Implementation Plan

A multi-step plan to implement a **Windows desktop GUI** for mounting Linux disks/partitions into WSL and mapping them as Windows drive letters, reusing `Mount-LinuxDisk.ps1` logic.

Overall architecture: **C# WPF (.NET 8)** GUI + **PowerShell helper scripts**. Privilege separation: elevated PowerShell for `wsl --mount`, non-elevated for drive-letter mapping.

---

## Milestone 0 – Context & Requirements

### 0.1 Goals

- **Goal**: Provide a GUI app ("LiMount") that lets a user:
  - Select a **physical disk** that likely contains Linux filesystems.
  - Select a **partition** on that disk, with label (if any) or `Partition N` otherwise.
  - Select a **free drive letter** (Z→A default) for mapping.
  - Click **Mount** to:
    - Mount the disk/partition into WSL using `wsl --mount`.
    - Detect the mounted path inside WSL and its UNC `\\wsl$` path.
    - Map that UNC path to the selected drive letter so it appears in Explorer.

### 0.2 Constraints & Assumptions

- Windows 10/11 with **WSL2** and support for `wsl --mount`.
- User is comfortable confirming a **UAC prompt** when disk mounting is required.
- Security is **not a primary concern** (prototype/MVP); we accept calling scripts with `ExecutionPolicy Bypass`.
- App should rely on **existing logic** from `Mount-LinuxDisk.ps1` and not re-implement WSL idiosyncrasies.

### 0.3 Existing Script Behavior (to reuse)

Key behavior from `Mount-LinuxDisk.ps1`:

- Requires **Administrator**.
- Enumerates physical disks via `Win32_DiskDrive` and shows:
  - `Index`, `DeviceID` (e.g. `\\.\PHYSICALDRIVE2`), `Model`, `SizeGB`.
- Prompts for disk index if not supplied.
- Safety checks with `Get-Disk`, `Get-Partition`, `Get-Volume`:
  - Aborts if disk is system/boot.
  - Warns if volumes with drive letters or NTFS filesystems are present.
- Calls `wsl --mount`:
  - `wsl --mount \\.\PHYSICALDRIVEn --partition X --type ext4|xfs`.
  - Detects "already mounted" vs. unsupported vs. generic failure.
- Determines WSL distro if not provided:
  - Tries `Get-ChildItem \\wsl$` first.
  - Falls back to `wsl -l -q`.
  - Cleans non-printable characters from distro name.
  - Verifies with `wsl -d <distro> -- pwd`.
- Computes WSL mount location:
  - Base: `/mnt/wsl` → `\\wsl$\\<DistroName>\\mnt\\wsl`.
  - Lists `/mnt/wsl` via `wsl -d <distro> -- ls -1 /mnt/wsl`.
  - Heuristically selects directory containing `PHYSICALDRIVE<DiskIndex>`.
- Drive-letter mapping logic (optional `-DriveLetter`):
  - Normalizes letter.
  - Chooses target UNC: specific mount folder if known, else base `/mnt/wsl` share.
  - Validates UNC path accessible.
  - Checks if the drive letter is in use via:
    - `net use X:` (network drive).
    - `subst X:` (substitution).
    - `Get-PSDrive X` (generic PS drive).
  - If mapped to same target → unmap & remap; if mapped to different target → error.
  - Maps drive letter:
    - `net use X: <UNC>` (preferred, persistent/network).
    - If fails, `New-PSDrive -Name X -PSProvider FileSystem -Root <UNC>` (session-only).

The app should reuse this behavior, but **separate**:

- WSL disk mount + distro/mount-path detection (elevated helper).
- Drive-letter mapping (non-elevated helper).

Verification of Milestone 0:

- [ ] Document stored in `limount-plan.md` (this file).
- [ ] Requirements and current script behavior accurately captured.

---

## Milestone 1 – Project Skeleton & Tooling

### 1.1 Create Solution Structure

Target root directory (suggested): `C:\Users\Rebecca\CascadeProjects\LiMount`.

Projects:

- **LiMount.App** – WPF (.NET 8) desktop app
  - Output: `LiMount.App.exe`.
- **LiMount.Core** – .NET class library (optional but recommended)
  - Contains models, services, helper logic for disk enumeration, calling scripts.
  - LiMount.App references this project.
- **scripts** folder (at solution root)
  - `Mount-LinuxDiskCore.ps1` – elevated helper
  - `Map-WSLShareToDrive.ps1` – non-elevated helper

### 1.2 .NET & Dependencies

- Use **.NET 8** (or latest supported LTS at implementation time).
- WPF app uses:
  - `System.Management` (for WMI/CIM) or `Microsoft.Management.Infrastructure`.
  - `System.Diagnostics.Process` for running PowerShell scripts.
- No external UI frameworks required (keep it simple, built-in WPF controls).

### 1.3 Verification

- [ ] `LiMount.sln` with projects `LiMount.App` and `LiMount.Core` builds successfully.
- [ ] `scripts` folder exists with placeholder `.ps1` files.
- [ ] App runs and shows a simple placeholder window.

---

## Milestone 2 – PowerShell Helpers Design

### 2.1 Elevated Helper: Mount-LinuxDiskCore.ps1

**Purpose**: Accept parameters from the GUI, perform safe `wsl --mount`, distro detection, and mount path discovery, then output machine-readable info.

**Location**: `scripts/Mount-LinuxDiskCore.ps1`

**Parameters**:

- `[int]$DiskIndex` – physical disk index (from `Win32_DiskDrive.Index`).
- `[int]$Partition` – partition number (>=1).
- `[ValidateSet("ext4","xfs")] [string]$FsType = "ext4"` – filesystem type for `wsl --mount`.
- `[string]$DistroName` – optional; if not supplied, use same auto-detection logic as current script.

**Behavior outline**:

1. **Require Administrator**
   - Same check as current script:
     - `WindowsPrincipal.IsInRole(Administrator)`.
2. **Enumerate disks** via `Get-CimInstance Win32_DiskDrive` and locate `$DiskIndex`.
3. **Safety checks** (reuse from current script):
   - `Get-Disk -Number` → abort if `IsSystem` or `IsBoot`.
   - Optional warnings about NTFS/drive-letter volumes (logging only).
4. **Validate partition** (`$Partition -ge 1`).
5. **Build physical disk path**:
   - `\\.\PHYSICALDRIVE<DiskIndex>`.
6. **Execute `wsl --mount`** (same logic as existing script):
   - `wsl --mount $diskPath --partition $Partition --type $FsType 2>&1`.
   - Handle:
     - Already mounted cases by text and exit code.
     - Unsupported `wsl --mount`.
     - Generic failures → write error and exit with non-zero code.
7. **Determine distro** (reuse logic):
   - Try `Get-ChildItem \\wsl$` to find distro names.
   - Fallback to `wsl -l -q`.
   - Clean non-printable characters.
   - Sanity check with `wsl -d <distro> -- pwd`.
8. **Determine mount folder path**:
   - Base path: `/mnt/wsl` → base UNC: `\\wsl$\\$DistroName\\mnt\\wsl`.
   - `wsl -d $DistroName -- ls -1 /mnt/wsl`.
   - Find directory name containing `PHYSICALDRIVE$DiskIndex` (same heuristic as current script).
   - If found, `MountPathLinux = "/mnt/wsl/<dir>"`, `MountPathUNC = "\\\\wsl$\\$DistroName\\mnt\\wsl\\<dir>"`.
   - If not found, fallback to base only: `MountPathLinux = "/mnt/wsl"`, `MountPathUNC = "\\\\wsl$\\$DistroName\\mnt\\wsl"`.
9. **Output machine-readable result**
   - Prefer simple key=value lines to simplify parsing, e.g.:
     ```text
     STATUS=OK
     DistroName=Ubuntu
     MountPathLinux=/mnt/wsl/PHYSICALDRIVE2p1
     MountPathUNC=\\wsl$\Ubuntu\mnt\wsl\PHYSICALDRIVE2p1
     ```
   - On error, output e.g.:
     ```text
     STATUS=ERROR
     ErrorMessage=<first-line-or-sanitized-error>
     ```
   - Use `exit 0` for success, `exit 1` for errors.

### 2.2 Non-Elevated Helper: Map-WSLShareToDrive.ps1

**Purpose**: Map a UNC path (`\\wsl$` share) to a drive letter in the **user session**, reusing existing mapping logic.

**Location**: `scripts/Map-WSLShareToDrive.ps1`

**Parameters**:

- `[string]$DriveLetter` – e.g. `"L"` or `"L:"`.
- `[string]$TargetUNC` – e.g. `"\\wsl$\\Ubuntu\\mnt\\wsl\\PHYSICALDRIVE2p1"`.

**Behavior outline**:

1. Normalize `DriveLetter`:
   - Strip trailing `:`.
   - Uppercase.
   - Validate regex `^[A-Z]$`.
2. Validate `$TargetUNC` is non-empty.
3. Verify target reachable: `Test-Path $TargetUNC`.
4. Check existing mapping (reuse logic from existing script):
   - `net use X:` → parse remote name if exists.
   - `subst X:` → parse substitution.
   - `Get-PSDrive X` → detect existing PS drive.
5. Conflict resolution:
   - If mapped to same target → unmap then continue.
   - If mapped to different target → output `STATUS=ERROR`, `ErrorMessage=Drive letter in use`, exit 1.
6. Perform mapping:
   - Try `net use X: $TargetUNC` first.
   - If success, verify `Test-Path "X:\"`.
   - If fails or drive not accessible, delete mapping and fallback to `New-PSDrive -Name X -PSProvider FileSystem -Root $TargetUNC`.
7. Output machine-readable result:
   - On success:
     ```text
     STATUS=OK
     DriveLetter=L
     MappedTo=\\wsl$\Ubuntu\mnt\wsl\PHYSICALDRIVE2p1
     ```
   - On error:
     ```text
     STATUS=ERROR
     ErrorMessage=<message>
     ```

### 2.3 Verification

- [ ] `Mount-LinuxDiskCore.ps1` runs independently with explicit parameters and produces expected key=value output.
- [ ] `Map-WSLShareToDrive.ps1` correctly maps/unmaps drive letters in a normal (non-elevated) PowerShell session.

---

## Milestone 3 – Disk & Partition Enumeration in `C#`

### 3.1 Models

In `LiMount.Core` define models:

- `DiskInfo`:
  - `int Index`
  - `string DeviceId` (e.g. `\\.\PHYSICALDRIVE2`)
  - `string Model`
  - `long SizeBytes`
  - `bool IsSystem`
  - `bool IsBoot`
  - `IReadOnlyList<PartitionInfo> Partitions`

- `PartitionInfo`:
  - `int PartitionNumber`
  - `long SizeBytes`
  - `string? Label` (GPT name or filesystem label if available)
  - `string? FileSystemType` (NTFS, FAT32, ext4-like markers, etc. where detectable)
  - `bool HasDriveLetter`
  - `char? DriveLetter`
  - `bool IsLikelyLinux` (heuristic flag)

### 3.2 Service: DiskEnumerationService

Class `DiskEnumerationService` in `LiMount.Core`:

- Method `IReadOnlyList<DiskInfo> GetDisks()`:
  - Use `System.Management.ManagementObjectSearcher` or CIM to query `Win32_DiskDrive` to get base disk list.
  - For each disk:
    - Query `MSFT_Disk` (via WMI/CIM) for `IsSystem`, `IsBoot`, partition style.
    - Query `MSFT_Partition` / `Get-Partition` for partitions.
    - Query `Get-Volume` or `Win32_Volume` for volume info (drive letters, filesystem labels when present).
  - Derive `IsLikelyLinux`:
    - True if:
      - No drive letter; and
      - Filesystem not recognized by Windows (e.g. no FS type, or type suggests Linux); and/or
      - PartitionType GUID or MBR type matches known Linux partition types.

- Method `IReadOnlyList<DiskInfo> GetCandidateDisks()`:
  - Filter out `IsSystem` or `IsBoot` disks.
  - Optionally only keep disks with at least one `PartitionInfo.IsLikelyLinux == true`.

### 3.3 Verification

- [ ] Diagnostic console output or temporary UI view listing disks, partitions, and `IsLikelyLinux` flag.
- [ ] Confirm system/boot disks are excluded from candidate list.

---

## Milestone 4 – Drive Letter Enumeration

### 4.1 Models & Service

- In `LiMount.Core`, define `DriveLetterInfo`:
  - `char Letter`
  - `bool IsInUse`
  - `string? Description` (e.g. `"Local Disk"`, `"Network mapping to ..."`).

- Service `DriveLetterService`:
  - Method `IReadOnlyList<char> GetUsedLetters()`:
    - Use `DriveInfo.GetDrives()` to get existing drives.
    - Optionally call `net use` and `subst` to detect network/subst mappings.
  - Method `IReadOnlyList<char> GetFreeLetters()`:
    - For `A..Z`, exclude used letters.
    - Return sorted in **descending** order (Z→A).

### 4.2 Verification

- [ ] Basic console or debug output showing free letters.
- [ ] Confirm that letters with network mappings are treated as used.

---

## Milestone 5 – WPF UI: Basic Layout & Data Binding

### 5.1 Main Window Layout

In `LiMount.App` (XAML):

- **Main window** elements:
  - `ComboBox` for **disk selection** (bind to list of `DiskInfo`):
    - Display: `Index – Model – Size (GB)`.
  - `ComboBox` for **partition selection** (bind to selected disk's partitions, filtered by `IsLikelyLinux`):
    - Display: `<Label or "Partition N"> – Size (GB)`.
  - `ComboBox` for **drive letter selection** (bind to free letters):
    - Display: `L:` etc.
    - Sorted Z→A, default first item.
  - Optional: `ComboBox` for filesystem type (`ext4`/`xfs`) with default `ext4`.
  - TextBlock area for **status / logs**.
  - `Button` **Mount**.

### 5.2 ViewModel Design (MVVM-lite)

Create `MainViewModel` in `LiMount.App` or `LiMount.Core`:

- Properties:
  - `ObservableCollection<DiskInfo> Disks`
  - `DiskInfo? SelectedDisk`
  - `ObservableCollection<PartitionInfo> Partitions`
  - `PartitionInfo? SelectedPartition`
  - `ObservableCollection<char> FreeDriveLetters`
  - `char? SelectedDriveLetter`
  - `string SelectedFsType` (`"ext4"` by default)
  - `string StatusMessage`
  - `bool IsBusy`

- Commands:
  - `RefreshCommand` – re-enumerate disks and free drive letters.
  - `MountCommand` – orchestrate the mount & mapping flow.

### 5.3 Verification

- [ ] App window opens and populates disks/partitions/drive letters (even if heuristics are rough).
- [ ] Selecting disk changes partition list.
- [ ] Drive letter dropdown sorted Z→A with a reasonable default.

---

## Milestone 6 – Integrating Elevated Mount Helper

### 6.1 Process Invocation Strategy

- Use `System.Diagnostics.ProcessStartInfo` to run:
  - `FileName = "powershell.exe"`
  - `Verb = "runas"` to trigger UAC for elevation.
  - `Arguments` includes:
    - `-ExecutionPolicy Bypass`.
    - `-File "<full-path-to-scripts/Mount-LinuxDiskCore.ps1>"`.
    - Parameters: `-DiskIndex`, `-Partition`, `-FsType`, optional `-DistroName`.
  - `UseShellExecute = true` and no need to capture standard output if using a temp file for results; alternatively, use `UseShellExecute = false` and capture stdout.

### 6.2 Result Parsing

- Prefer capturing **stdout** and parsing key=value lines.
- A small utility class `KeyValueOutputParser`:
  - Input: string array of lines.
  - Returns: `Dictionary<string,string>`.

- Model `MountResult`:
  - `bool Success`
  - `string? DistroName`
  - `string? MountPathLinux`
  - `string? MountPathUNC`
  - `string? ErrorMessage`

### 6.3 Flow in `MountCommand`

1. Validate that `SelectedDisk`, `SelectedPartition`, `SelectedDriveLetter` are set.
2. Set `IsBusy = true`, `StatusMessage = "Mounting partition in WSL..."`.
3. Start elevated process for `Mount-LinuxDiskCore.ps1`.
4. Wait for completion.
5. Parse output into `MountResult`.
6. If failure, show `ErrorMessage` and stop.
7. If success, proceed to mapping (Milestone 7).

### 6.4 Verification

- [ ] When clicking `Mount`, UAC prompt appears (if not already elevated).
- [ ] On success, the helper script reports valid UNC path.
- [ ] On known error conditions (no WSL, unsupported `wsl --mount`), errors show in status area.

---

## Milestone 7 – Integrating Drive Mapping Helper

### 7.1 Non-Elevated Script Call

- From the GUI (running as the normal user), call:
  - `powershell.exe -ExecutionPolicy Bypass -File "Map-WSLShareToDrive.ps1" -DriveLetter L -TargetUNC "\\wsl$\\..."`.
- Use `ProcessStartInfo` with:
  - `UseShellExecute = false`.
  - `RedirectStandardOutput = true` / `RedirectStandardError = true`.

### 7.2 Mapping Result Model

- `MappingResult`:
  - `bool Success`
  - `char DriveLetter`
  - `string? TargetUNC`
  - `string? ErrorMessage`

### 7.3 Full Mount Flow (UI-level)

1. User clicks **Mount**.
2. Elevated helper mounts disk in WSL and returns `MountPathUNC`.
3. GUI verifies UNC path reachable (optional quick check).
4. GUI calls `Map-WSLShareToDrive.ps1` with selected drive letter and UNC.
5. Parse output, update `StatusMessage`:
   - On success: `"Mounted as L:\"` and optionally provide an **Open in Explorer** button.
   - On error: show error and suggest changing drive letter or checking WSL.

### 7.4 Verification

- [ ] After successful mount, a new drive appears in Explorer with the selected letter.
- [ ] If mapping fails due to letter being taken meanwhile, error is surfaced.

---

## Milestone 8 – UX, Logging, and Polish

### 8.1 UX Improvements

- Disable **Mount** button when:
  - `IsBusy = true`.
  - Required selections are missing.
- Show progress text during operations: `"Mounting in WSL..."`, `"Mapping drive letter..."`.
- Optional: show a minimal log console area (multi-line TextBox) to append script outputs.

### 8.2 Error-handling & Safety

- Ensure that exceptions in process launching or parsing are caught and translated to friendly messages.
- Ensure the app never allows selecting system/boot disks.
- Optionally warn when a disk has existing NTFS/drive-letter volumes.

### 8.3 Settings (optional, MVP+)

- Persist last used:
  - Disk index.
  - Partition number.
  - Drive letter.
  - Distro name.
- Use `ApplicationSettings` or simple JSON file in `%AppData%`.

### 8.4 Verification

- [ ] Typical happy path feels smooth and fast.
- [ ] Errors are actionable and do not crash the app.

---

## Milestone 9 – Testing & Smoke Verification

### 9.1 Manual Smoke Tests

- **Positive path**:
  - Disk with Linux partition.
  - Partition is not system/boot.
  - Drive letter free.
  - Mount + mapping succeed, drive appears in Explorer.

- **Negative paths**:
  - Attempt to select system/boot disk → disk never appears in candidate list.
  - Unsupported Windows/WSL (no `wsl --mount`) → meaningful error.
  - Target UNC not reachable (WSL stopped) when mapping → mapping error from helper surfaced.
  - Drive letter taken between enumeration and mapping → helper reports conflict, UI shows it.

### 9.2 Automated Tests (minimal, MVP)

- Unit tests for:
  - Disk enumeration heuristics (`IsLikelyLinux`).
  - Drive letter enumeration (given mocked used letters, correct free list Z→A).
  - Key=value parsing logic for script outputs.

### 9.3 Verification

- [ ] Basic unit tests pass.
- [ ] Manual smoke tests confirm end-to-end functionality.

---

## Milestone 10 – Packaging (Optional)

- Create a simple **self-contained** publish profile for `LiMount.App`.
- Ship `scripts/` folder alongside the executable.
- Ensure script paths are resolved relative to the executable location.

Verification:

- [ ] A single-folder publish contains `LiMount.App.exe` and helper scripts.
- [ ] Running `LiMount.App.exe` from that folder works without additional setup.
