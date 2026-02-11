# Incident: WSL XFS Mount Regression and Recovery

Date: 2026-02-11

## Summary

LiMount reported mount failures and inconsistent mounted/unmounted state behavior for a second-drive root partition. Investigation found two separate issues:

1. A real filesystem/kernel compatibility limit in WSL for the target XFS partition.
2. App/script reliability gaps that could produce false mount-state signals or false mapping verification failures.

## Root Cause (Filesystem Compatibility)

`wsl --mount` failed with `Invalid argument` and `dmesg` reported:

- `Superblock has unknown incompatible features`
- `Filesystem cannot be safely mounted by this kernel`

Conclusion: the XFS feature set on that partition is unsupported by the installed WSL kernel.

## Product Fixes Applied

### Mount + Diagnostics

- `Mount-LinuxDiskCore.ps1` now emits structured diagnostics:
  - `ErrorCode`
  - `ErrorHint`
  - `DmesgSummary`
  - `AlreadyMounted`
  - `UncVerified`
- Invalid-argument mount failures now classify unsupported XFS as `XFS_UNSUPPORTED_FEATURES`.

### Detection + State Reliability

- Startup mount detection now checks live WSL mount table output instead of only listing `/mnt/wsl`.
- Stale `PHYSICALDRIVE*p*` directories are detected and cleaned up best-effort.
- Unmount paths treat `Wsl/Service/DetachDisk/ERROR_FILE_NOT_FOUND` as already-detached success.

### Mapping Reliability

- `Map-WSLShareToDrive.ps1` verification now normalizes UNC variants (including `UNC\...` from `subst`) before equality checks.

### Test Automation

- Added deterministic WinUI UI tests (`LiMount.UITests`) with unsupported-XFS and success scenarios.
- Added hardware-in-loop script: `scripts/run-hil-mount-test.ps1` with:
  - expected-failure scenario mode
  - full drive-level verification mode

## Verification Results

Validated on real hardware:

- Expected failure path:
  - `Disk 1, Partition 2` => unsupported XFS diagnostics surfaced correctly.
- Success path:
  - `Disk 1, Partition 1` => mount, UNC access, and unmount all succeeded.

Validation commands:

```powershell
dotnet test LiMount.Tests
dotnet test LiMount.UITests
powershell -ExecutionPolicy Bypass -File .\scripts\run-hil-mount-test.ps1 -DiskIndex 1 -VerifyDriveEndToEnd -FailurePartition 2
```

## Follow-up

- Consider VM fallback (for unsupported WSL filesystem features) as a future enhancement.
- Keep release trimming disabled until trim-safe binding migration is complete.
