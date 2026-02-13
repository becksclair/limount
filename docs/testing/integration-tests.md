# Integration Testing Guide for LiMount

This document describes integration testing for LiMount in real Windows + WSL environments.

LiMount now supports three Windows access modes:
- `NetworkLocation` (default)
- `DriveLetterLegacy`
- `None`

---

## Testing Strategy Overview

### Unit and Service Tests (Automated, Cross-Platform)

**Location**: `LiMount.Tests/`  
**Run on**: Any platform  
**Coverage**: core business logic, contracts, orchestration, state/history, validation

Run:

```powershell
dotnet test LiMount.Tests
```

### UI Automation Tests (Automated, Windows-only runner)

**Project**: `LiMount.UITests/`  
**Framework**: xUnit + FlaUI UIA3  
**Mode**: deterministic test mode via env vars

Run:

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

Full screenshot batch:

```powershell
$env:LIMOUNT_RUN_UI_TESTS='1'
$env:LIMOUNT_CAPTURE_SCREENSHOT_BATCH='1'
dotnet test LiMount.UITests --filter "FullyQualifiedName~ScreenshotBatchUiTests"
Remove-Item Env:\LIMOUNT_RUN_UI_TESTS
Remove-Item Env:\LIMOUNT_CAPTURE_SCREENSHOT_BATCH
```

### Hardware-in-loop Tests (Windows-only, real disk + WSL)

Use only on machines with admin access and explicitly chosen non-system test disks.

Run expected-failure validation:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\run-hil-mount-test.ps1 -DiskIndex 1 -Partition 2 -ExpectXfsUnsupported
```

Run drive-level verification:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\run-hil-mount-test.ps1 -DiskIndex 1 -VerifyDriveEndToEnd -FailurePartition 2
```

---

## Integration Test Scenarios

### Scenario 1: Basic Mount/Unmount (Default Network Location)

**Estimated time**: 2-3 minutes

**Steps**:
1. Launch LiMount.
2. Refresh and select a disk + Linux partition.
3. Confirm access mode is `NetworkLocation` (default).
4. Click **Mount** and accept UAC prompt.
5. Wait for success status.
6. Click **Open in Explorer**.
7. Verify UNC content is accessible.
8. Click **Unmount**.
9. Verify cleanup succeeds and UI returns to ready state.

**Expected results**:
- `NetworkLocation` mount succeeds with no drive-letter selection.
- Explorer opens mounted content.
- Unmount removes network-location integration and WSL mount.

**Validation commands**:

```powershell
wsl -e ls /mnt/wsl/
Test-Path \\wsl$\Ubuntu\mnt\wsl\PHYSICALDRIVE1p1
Test-Path "$env:APPDATA\Microsoft\Windows\Network Shortcuts"
```

---

### Scenario 2: Legacy Drive-Letter Mode

**Estimated time**: 2-3 minutes

**Steps**:
1. Set access mode to `DriveLetterLegacy`.
2. Select a free drive letter.
3. Mount and confirm success.
4. Open in Explorer and validate `<DriveLetter>:\`.
5. Unmount and verify mapping is removed.

**Expected results**:
- Drive letter is required only in legacy mode.
- Mapping appears and is removed correctly.

**Validation commands**:

```powershell
subst
Get-PSDrive -PSProvider FileSystem | Where-Object { $_.Name -eq "Z" }
```

---

### Scenario 3: None Mode (Mount-only)

**Estimated time**: 1-2 minutes

**Steps**:
1. Set access mode to `None`.
2. Mount selected disk/partition.
3. Verify mount success.
4. Verify **Open in Explorer** is disabled.
5. Unmount.

**Expected results**:
- Mount/unmount works with no Windows integration script calls.
- Explorer action is unavailable in this mode.

---

### Scenario 4: State Persistence and Restore

**Estimated time**: 3-4 minutes

**Steps**:
1. Mount successfully in each mode (`NetworkLocation`, `DriveLetterLegacy`, `None`) across separate runs.
2. Restart app after each mount.
3. Verify active mount is detected and shown with correct access metadata.
4. Unmount after restore.

**Expected results**:
- Restored status reflects mode-aware details.
- Non-legacy mounts are not pruned due to missing drive letters.

**Validation commands**:

```powershell
Test-Path "$env:LocalAppData\LiMount\mount-state.json"
Get-Content "$env:LocalAppData\LiMount\mount-state.json" | ConvertFrom-Json
```

---

### Scenario 5: History Metadata Validation

**Estimated time**: 1-2 minutes

**Steps**:
1. Perform mount/unmount in each access mode.
2. Open history view.
3. Verify entries include mode-specific metadata:
   - `DriveLetterLegacy`: drive letter
   - `NetworkLocation`: network location name
   - `None`: explicit no-access indication

**Expected results**:
- History data is complete and mode-aware.

**Validation commands**:

```powershell
Test-Path "$env:LocalAppData\LiMount\mount-history.json"
Get-Content "$env:LocalAppData\LiMount\mount-history.json" | ConvertFrom-Json
```

---

### Scenario 6: Access-Mode Error Handling

**Estimated time**: 2-3 minutes

**Checks**:
- Attempt legacy mount without drive letter -> validation error.
- Attempt with an already-used drive letter in legacy mode -> conflict handling.
- Invalid disk/partition selection -> no crash, actionable error.
- WSL stopped (`wsl --shutdown`) -> environment failure is surfaced.

---

### Scenario 7: Script Contract Verification

**Estimated time**: 3-4 minutes

**Purpose**: Validate key-value outputs and mode routing.

**Checks**:
- `NetworkLocation` mode calls:
  - `scripts\network\Create-NetworkLocation.ps1`
  - `scripts\network\Remove-NetworkLocation.ps1`
- `DriveLetterLegacy` mode calls:
  - `scripts\Map-WSLShareToDrive.ps1`
  - `scripts\Unmap-DriveLetter.ps1`
- `None` mode performs no Windows integration action.

---

## Manual Release Checklist

### Happy Path

- [ ] App launches without errors
- [ ] Default mount works in `NetworkLocation` mode
- [ ] Legacy mount works in `DriveLetterLegacy` mode
- [ ] Mount/unmount works in `None` mode
- [ ] History entries include mode metadata
- [ ] State restore reflects mounted mode correctly

### Error Path

- [ ] No disk selected -> blocked or clear validation
- [ ] WSL not running -> actionable error
- [ ] Legacy mount with used drive letter -> conflict handled
- [ ] Legacy mount without drive letter -> validation error
- [ ] Unmount missing mount -> graceful handling

### UI and Screenshot Validation

- [ ] `ScreenshotBatchUiTests` passes in test mode
- [ ] Output exists under `screenshots\ui-batch\<timestamp>\`
- [ ] All deterministic screenshot files are present and non-empty
- [ ] Default main-page captures reflect hidden drive-letter picker

---

## Integration Gaps and Constraints

| Scenario | Reason | Mitigation |
|----------|--------|------------|
| WMI disk enumeration | Windows-only API | Run HIL/manual tests on Windows |
| UAC elevation path | Full unattended UAC automation is limited | Use HIL runner defaults |
| WSL mount/unmount | Requires local WSL2 + compatible disk | HIL/manual validation |
| NetHood behavior timing | Explorer refresh timing can vary | Validate with retries and manual Explorer checks |
| WinUI interaction fidelity | Requires UI automation on Windows desktop session | FlaUI deterministic tests + manual pass |

---

## Recommended Verification Commands

```powershell
dotnet build --nologo
dotnet test LiMount.Tests --nologo
dotnet test LiMount.Tests --nologo --filter FullyQualifiedName~MountOrchestratorTests
dotnet test LiMount.Tests --nologo --filter FullyQualifiedName~UnmountOrchestratorTests
dotnet test LiMount.Tests --nologo --filter FullyQualifiedName~MainViewModelTests
$env:LIMOUNT_RUN_UI_TESTS='1'; dotnet test LiMount.UITests --nologo; Remove-Item Env:\LIMOUNT_RUN_UI_TESTS
$env:LIMOUNT_RUN_UI_TESTS='1'; $env:LIMOUNT_CAPTURE_SCREENSHOT_BATCH='1'; dotnet test LiMount.UITests --nologo --filter FullyQualifiedName~ScreenshotBatchUiTests; Remove-Item Env:\LIMOUNT_RUN_UI_TESTS; Remove-Item Env:\LIMOUNT_CAPTURE_SCREENSHOT_BATCH
```

---

**Last Updated**: 2026-02-13  
**Maintained By**: Development Team  
**Review Cycle**: Before each release
