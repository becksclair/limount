# Integration Testing Guide for LiMount

This document describes integration testing for LiMount. Since LiMount is a Windows-only WPF application that interacts with WSL, WMI, and PowerShell, comprehensive integration testing requires a Windows environment.

---

## Testing Strategy Overview

### Unit Tests (Automated, Cross-Platform)

**Location**: `LiMount.Tests/`
**Run on**: Any platform (Linux, Windows, macOS)
**Coverage**: Business logic, validation, models, result objects

**What we test**:
- ✅ Service interfaces and contracts
- ✅ Validation logic (parameter checking)
- ✅ Result object creation (FromDictionary, CreateSuccess, CreateFailure)
- ✅ Configuration parsing (IOptions integration)
- ✅ State management (MountStateService with mocked file system)
- ✅ History logging (MountHistoryService with mocked file system)

**What we mock**:
- IScriptExecutor (PowerShell execution)
- IDiskEnumerationService (WMI queries)
- File system operations (for state/history services)
- IDialogService (MessageBox abstraction)

**Running unit tests**:
```bash
# On any platform
cd LiMount.Tests
dotnet test

# With coverage
dotnet test --collect:"XCode Code Coverage"
```

**Current status**: 27+ unit tests, 85%+ coverage on testable logic

---

### Integration Tests (Manual, Windows-Only)

**Environment**: Windows 11 (Build 22000+) with WSL2 installed
**Coverage**: End-to-end workflows, PowerShell scripts, WMI queries, actual mounting

**What we test**:
- Disk enumeration (real WMI queries)
- PowerShell script execution (elevated and non-elevated)
- WSL mount/unmount operations
- Drive letter mapping
- UNC path accessibility
- State persistence across restarts
- History logging
- UI workflows

**Why not automated?**
- Requires Administrator elevation (can't automate UAC)
- Requires WSL2 with at least one distro installed
- Requires physical or virtual disks to mount
- WPF UI testing requires Windows
- PowerShell execution is OS-specific

---

## Integration Test Scenarios

### Scenario 1: Basic Mount and Map Workflow

**Estimated time**: 2-3 minutes

**Prerequisites**:
- Windows 11 with WSL2 installed
- At least one WSL distro (Ubuntu recommended)
- External USB drive or virtual disk with Linux partition (ext4)
- Administrator privileges

**Steps**:
1. Launch LiMount application
2. Click "Refresh" button
3. Verify disk list populates with available disks
4. Select a disk with Linux partition
5. Select the Linux partition
6. Select an available drive letter (e.g., Z:)
7. Select filesystem type (ext4)
8. Click "Mount" button
9. Accept UAC elevation prompt
10. Wait for mount to complete (progress messages should appear)
11. Verify drive appears in Windows Explorer
12. Open drive in Explorer (click "Open in Explorer" button)
13. Verify you can read/write files
14. Close Explorer
15. Click "Unmount" button
16. Verify drive disappears from Explorer
17. Verify status shows "Ready"

**Expected Results**:
- ✅ Disk enumeration shows Linux partitions
- ✅ Mount completes successfully with progress updates
- ✅ Drive letter appears in Explorer
- ✅ Files are accessible (read/write)
- ✅ Unmount completes successfully
- ✅ Drive letter disappears from Explorer
- ✅ No error messages

**Validation**:
```powershell
# Verify mount in WSL
wsl -e ls /mnt/wsl/

# Verify drive mapping
Get-PSDrive -PSProvider FileSystem | Where-Object { $_.Name -eq "Z" }

# Verify UNC path
Test-Path \\wsl$\Ubuntu\mnt\wsl\PHYSICALDRIVE1p1
```

---

### Scenario 2: Mount State Persistence

**Estimated time**: 3-4 minutes

**Purpose**: Verify that mount state survives application restart

**Steps**:
1. Mount a disk (follow Scenario 1 steps 1-10)
2. Close LiMount application
3. Open Windows Explorer, verify drive is still accessible
4. Re-launch LiMount application
5. Verify "Unmount" button is visible (app detected existing mount)
6. Verify status shows mount information
7. Click "Unmount" button
8. Verify unmount succeeds

**Expected Results**:
- ✅ Mount persists after app close
- ✅ App detects existing mount on startup
- ✅ UI reflects mount state correctly
- ✅ Unmount works after restart

**Validation**:
```powershell
# Check state file exists
Test-Path "$env:LocalAppData\LiMount\mount-state.json"

# Read state file
Get-Content "$env:LocalAppData\LiMount\mount-state.json" | ConvertFrom-Json
```

---

### Scenario 3: Mount History Tracking

**Estimated time**: 1-2 minutes

**Purpose**: Verify that all mount/unmount operations are logged

**Steps**:
1. Mount a disk successfully
2. Unmount the disk
3. Click "View History" button
4. Verify history window shows both operations
5. Verify timestamps are correct
6. Verify status (Success/Failure) is correct
7. Verify disk/partition/drive information is accurate
8. Click "Close" button

**Expected Results**:
- ✅ History window opens
- ✅ Mount operation logged with "Success" status
- ✅ Unmount operation logged with "Success" status
- ✅ All details are accurate (disk, partition, drive, timestamp)

**Validation**:
```powershell
# Check history file exists
Test-Path "$env:LocalAppData\LiMount\mount-history.json"

# Read history file
Get-Content "$env:LocalAppData\LiMount\mount-history.json" | ConvertFrom-Json
```

---

### Scenario 4: Error Handling - Invalid Disk Selection

**Estimated time**: 30 seconds

**Purpose**: Verify graceful error handling for invalid selections

**Steps**:
1. Launch LiMount
2. Don't select any disk (leave combo box empty)
3. Click "Mount" button

**Expected Results**:
- ✅ Mount button should be disabled (CanExecute prevents click)
- ✅ OR: Validation error message displayed
- ✅ No crash or exception

**Alternative Test**:
1. Select a disk
2. Select a partition
3. Manually eject/disconnect the disk
4. Click "Mount" button

**Expected Results**:
- ✅ Error message displayed ("Disk not found" or similar)
- ✅ Status shows error
- ✅ No crash

---

### Scenario 5: Error Handling - WSL Not Running

**Estimated time**: 1 minute

**Purpose**: Verify environment validation detects WSL issues

**Steps**:
1. Stop all WSL instances: `wsl --shutdown`
2. Launch LiMount
3. Attempt to mount a disk

**Expected Results**:
- ✅ Error message: "WSL is not running" or "No WSL distro found"
- ✅ Helpful guidance on how to fix (start WSL, install distro)
- ✅ No crash

**Validation**:
```powershell
# Verify WSL is stopped
wsl --list --verbose  # Should show "Stopped"
```

---

### Scenario 6: Error Handling - Drive Letter Already in Use

**Estimated time**: 2 minutes

**Purpose**: Verify detection of drive letter conflicts

**Steps**:
1. Map a network drive to Z: manually
   ```powershell
   net use Z: \\someserver\someshare
   ```
2. Launch LiMount
3. Try to mount a disk to Z:
4. Verify Z: is NOT in the "Available Drive Letters" list

**Expected Results**:
- ✅ Z: does not appear in drive letter dropdown
- ✅ Only truly available letters are shown
- ✅ No conflict occurs

---

### Scenario 7: Concurrent Mounts

**Estimated time**: 4-5 minutes

**Purpose**: Verify multiple disks can be mounted simultaneously

**Steps**:
1. Connect multiple USB drives with Linux partitions (or use virtual disks)
2. Mount first disk to Z:
3. Refresh disk list
4. Mount second disk to Y:
5. Verify both drives appear in Explorer
6. Unmount both drives

**Expected Results**:
- ✅ Both disks mount successfully
- ✅ Both drive letters appear in Explorer
- ✅ State file tracks both mounts
- ✅ Both can be unmounted independently
- ✅ No interference between mounts

---

### Scenario 8: PowerShell Script Execution

**Estimated time**: 3-4 minutes

**Purpose**: Verify PowerShell scripts execute correctly with elevation

**Steps**:
1. Enable PowerShell script logging:
   ```powershell
   Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser
   ```
2. Mount a disk (triggers Mount-LinuxDiskCore.ps1)
3. Check temp file was created and contains output:
   ```powershell
   Get-ChildItem $env:TEMP | Where-Object { $_.Name -like "limount_mount_*" }
   ```
4. Map drive (triggers Map-WSLShareToDrive.ps1)
5. Unmount (triggers Unmount-LinuxDisk.ps1 and Unmap-DriveLetter.ps1)

**Expected Results**:
- ✅ Elevated scripts create temp files in %TEMP%
- ✅ Temp files contain key=value output
- ✅ Non-elevated scripts output directly to stdout
- ✅ All scripts complete successfully
- ✅ Temp files are cleaned up after operation

**Validation**:
```powershell
# Check for leftover temp files (should be cleaned up)
Get-ChildItem $env:TEMP | Where-Object { $_.Name -like "limount_*" }

# Should return empty
```

---

### Scenario 9: Logging Verification

**Estimated time**: 2-3 minutes

**Purpose**: Verify Serilog file logging works in production mode

**Steps**:
1. Build application in Release mode
2. Run application (not from debugger)
3. Perform mount/unmount operations
4. Check log file exists:
   ```powershell
   Get-ChildItem "$env:LocalAppData\LiMount\logs"
   ```
5. Open log file, verify entries exist
6. Verify log level is appropriate (Info, Warning, Error)

**Expected Results**:
- ✅ Log file exists in %LocalAppData%\LiMount\logs\
- ✅ Log file named: limount-YYYYMMDD.log
- ✅ Log entries include timestamps, level, message
- ✅ Errors are logged with stack traces
- ✅ Log rotation works (new file daily)

**Validation**:
```powershell
# Check log file content
Get-Content "$env:LocalAppData\LiMount\logs\limount-*.log" | Select-Object -First 20
```

---

### Scenario 10: Configuration Customization

**Estimated time**: 2-3 minutes

**Purpose**: Verify appsettings.json configuration is respected

**Steps**:
1. Close LiMount
2. Edit `appsettings.json`:
   ```json
   {
     "LiMount": {
       "MountOperations": {
         "UncAccessibilityRetries": 10,
         "UncAccessibilityRetryDelayMs": 1000
       }
     }
   }
   ```
3. Restart LiMount
4. Mount a disk
5. Observe that retry behavior has changed (longer delays visible)

**Expected Results**:
- ✅ Configuration changes are loaded on startup
- ✅ Behavior reflects new configuration values
- ✅ No errors from invalid configuration

**Validation**:
```powershell
# Verify config file exists
Test-Path ".\LiMount.App\appsettings.json"

# Verify config is valid JSON
Get-Content ".\LiMount.App\appsettings.json" | ConvertFrom-Json
```

---

## Manual Testing Checklist

Before each release, manually test these critical paths:

### Happy Path

- [ ] Application launches without errors
- [ ] Disk enumeration shows Linux partitions
- [ ] Mount operation completes successfully
- [ ] Drive appears in Explorer
- [ ] Files are accessible (read/write test)
- [ ] Unmount operation completes successfully
- [ ] Drive disappears from Explorer
- [ ] History shows mount/unmount entries

### Error Paths

- [ ] Mount with no disk selected (validation error)
- [ ] Mount with WSL not running (environment error)
- [ ] Mount to already-used drive letter (conflict detected)
- [ ] Unmount non-existent mount (graceful error)
- [ ] Invalid configuration (app shows error, doesn't crash)

### Edge Cases

- [ ] Multiple concurrent mounts (all work independently)
- [ ] Application restart with active mount (state restored)
- [ ] Disk disconnected while mounted (error handled gracefully)
- [ ] PowerShell execution policy prevents scripts (clear error message)

### Performance

- [ ] Disk enumeration completes within 2 seconds
- [ ] Mount operation completes within 10 seconds (typical case)
- [ ] UI remains responsive during long operations
- [ ] Progress messages update smoothly

### UI/UX

- [ ] All buttons have correct enabled/disabled state
- [ ] Progress messages are clear and informative
- [ ] Error messages are actionable
- [ ] Tooltips explain purpose of each control
- [ ] Window can be resized, controls adjust properly

---

## Automated Integration Tests (Future)

While full integration testing currently requires manual testing on Windows, we can create automated integration tests using:

### Option 1: Windows-Only CI/CD

**Setup**: GitHub Actions with Windows runner

```yaml
name: Integration Tests

on: [push, pull_request]

jobs:
  integration-test:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v3
      - name: Setup .NET
        uses: actions/setup-dotnet@v3
        with:
          dotnet-version: 8.0.x
      - name: Install WSL
        run: |
          wsl --install -d Ubuntu
          wsl --set-default-version 2
      - name: Run Integration Tests
        run: dotnet test --filter "Category=Integration"
```

**Challenges**:
- GitHub Actions runners don't support nested virtualization (WSL won't work)
- Would need self-hosted Windows runner with WSL2
- Difficult to create test disks without physical hardware

### Option 2: PowerShell Pester Tests

**Setup**: Create Pester tests for PowerShell scripts

```powershell
# scripts/Tests/Mount-LinuxDiskCore.Tests.ps1
Describe "Mount-LinuxDiskCore" {
    It "Should validate disk index parameter" {
        { .\Mount-LinuxDiskCore.ps1 -DiskIndex -1 -Partition 1 -FsType "ext4" } | Should -Throw
    }

    It "Should output key=value format" {
        # Test with mocked wsl command
        Mock wsl { return "STATUS=OK" }
        $result = .\Mount-LinuxDiskCore.ps1 -DiskIndex 1 -Partition 1 -FsType "ext4"
        $result | Should -Contain "STATUS="
    }
}
```

**Run tests**:
```powershell
Invoke-Pester -Path .\scripts\Tests\
```

### Option 3: Approval Tests for Scripts

**Concept**: Capture script output, commit it as "approved" baseline, detect changes

```powershell
# Run script, capture output
$output = .\Mount-LinuxDiskCore.ps1 -DiskIndex 1 -Partition 1 -FsType "ext4" 2>&1

# Compare to approved baseline
$approved = Get-Content ".\approved\Mount-LinuxDiskCore.approved.txt"
if ($output -ne $approved) {
    throw "Output changed from approved baseline"
}
```

---

## Integration Test Gaps (Known Limitations)

Due to Windows-specific dependencies, these scenarios are NOT covered by automated tests:

| Scenario | Reason | Mitigation |
|----------|--------|------------|
| WMI disk enumeration | Requires Windows WMI | Manual testing on Windows |
| PowerShell elevation (UAC) | Can't automate UAC prompts | Manual testing with UAC |
| WSL mount/unmount | Requires actual WSL2 | Manual testing with WSL |
| Drive letter mapping | Windows-specific API | Manual testing on Windows |
| UNC path accessibility | Requires WSL running | Manual testing with WSL |
| WPF UI interactions | Requires Windows + UI automation | Manual UI testing |

**Best Practice**: Maximize unit test coverage (85%+) to catch logic errors, rely on manual integration tests for Windows-specific operations.

---

## Test Data Setup

### Creating a Test Disk with Linux Partition

#### Option 1: Virtual Disk (VHD)

```powershell
# Create VHD
$vhdPath = "C:\Temp\test-linux-disk.vhdx"
New-VHD -Path $vhdPath -SizeBytes 1GB -Dynamic

# Mount VHD
Mount-VHD -Path $vhdPath

# Get disk number
$disk = Get-VHD -Path $vhdPath
$diskNumber = $disk.Number

# Initialize disk (GPT)
Initialize-Disk -Number $diskNumber -PartitionStyle GPT

# Create partition (will be detected as Linux partition when formatted in WSL)
# Don't format in Windows - format in WSL with ext4

# In WSL:
wsl -e sudo mkfs.ext4 /dev/sdX1  # Replace X with appropriate letter
```

#### Option 2: USB Drive

1. Insert USB drive
2. Format in Linux/WSL with ext4:
   ```bash
   sudo mkfs.ext4 /dev/sdX1
   ```
3. Use for testing

#### Option 3: Docker Desktop with WSL2 Backend

Docker Desktop includes WSL2, which can be used for testing WSL integration.

---

## Reporting Integration Test Results

After manual testing, document results:

```markdown
## Integration Test Run - 2025-11-18

**Environment**:
- OS: Windows 11 Pro (Build 22621)
- WSL Version: 2.0.14.0
- Distro: Ubuntu 22.04 LTS
- .NET Version: 8.0.100

**Test Results**:

| Scenario | Status | Notes |
|----------|--------|-------|
| Basic Mount and Map | ✅ Pass | Completed in 4.2s |
| Mount State Persistence | ✅ Pass | State restored correctly |
| Mount History Tracking | ✅ Pass | Both operations logged |
| Invalid Disk Selection | ✅ Pass | Validation prevented mount |
| WSL Not Running | ⚠️ Warn | Error message could be clearer |
| Drive Letter Conflict | ✅ Pass | Conflict detected |
| Concurrent Mounts | ✅ Pass | Both disks mounted |
| PowerShell Scripts | ✅ Pass | All scripts executed correctly |
| Logging | ✅ Pass | Logs created and rotated |
| Configuration | ✅ Pass | Settings respected |

**Issues Found**:
- WSL error message could provide better guidance on how to start WSL
- Suggested: Add "Click here to start WSL" button in error dialog

**Test Coverage**: 10/10 scenarios passed, 1 improvement suggested
```

---

## Continuous Improvement

As the project matures, consider:

1. **Self-Hosted Windows Runner**: For automated integration tests
2. **Docker-based Testing**: Use Docker Desktop + WSL2 for reproducible environment
3. **Pester Test Suite**: PowerShell script testing
4. **UI Automation**: Windows Application Driver for WPF testing
5. **Performance Benchmarks**: Track mount/unmount operation times
6. **Compatibility Matrix**: Test on Windows 10 vs. 11, different WSL versions

---

**Last Updated**: 2025-11-18
**Maintained By**: Development Team
**Review Cycle**: Before each release
