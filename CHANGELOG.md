# Changelog

All notable changes to LiMount will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- Mount-script diagnostic contract fields: `ErrorCode`, `ErrorHint`, `DmesgSummary`, `AlreadyMounted`, `UncVerified`
- Deterministic UI automation project (`LiMount.UITests`) with WinUI test mode scenarios and optional screenshot capture
- Hardware-in-loop helper script: `scripts/run-hil-mount-test.ps1`
- Partition-scoped mount state APIs (`GetMountForDiskPartitionAsync`, `GetMountsForDiskAsync`, partition-specific unregister)
- Dedicated UNC existence timeout configuration: `UncExistenceCheckTimeoutMs`

### Changed

- Release publish profiles now disable trimming (`PublishTrimmed=false`) for reliability
- Filesystem detection uses disk-aware `lsblk` snapshot comparison (`NAME,PKNAME,FSTYPE`) rather than partition suffix matching
- Startup mount detection now checks live WSL mount table output instead of directory listing heuristics
- Mount orchestration retries failed explicit filesystem mounts once with `fsType=auto`
- Mapping verification now normalizes UNC variants (including `UNC\...` forms reported by `subst`)

### Fixed

- Fixed incorrect WSL distro-list invocation path (`wsl -l -q` now uses raw arguments, not `wsl -e -l -q`)
- Fixed false “already mounted” state caused by stale `/mnt/wsl/PHYSICALDRIVE*p*` directories
- Fixed unmount cleanup failures when WSL returns `Wsl/Service/DetachDisk/ERROR_FILE_NOT_FOUND` (treated as already detached)
- Fixed elevated `subst` verification failures from path-format mismatches
- Fixed rollback behavior to avoid unmounting mounts that existed before the current operation

### Validation

- Real-system HIL verification completed:
  - expected failure path validated for unsupported XFS root partition (`XFS_UNSUPPORTED_FEATURES`)
  - successful mount + unmount validated on an alternate partition on the same physical disk
- Full test suite passed (`dotnet test LiMount.Tests`)

### Architecture

- Clean architecture with DI, interfaces, and orchestration layer
- MVVM pattern with CommunityToolkit.Mvvm
- Separated concerns: Services → Orchestrators → ViewModels → Views
- PowerShell scripts for elevated (mount/unmount) and non-elevated (map/unmap) operations

### Technical

- .NET 10 WinUI 3 application (Windows-only)
- System.Management for WMI disk enumeration
- Microsoft.Extensions.DependencyInjection for IoC
- xUnit + Moq + FluentAssertions for testing

## [0.1.0] - 2025-11-25

### Added

- Initial release
- Basic mount/unmount workflow
- Disk and partition enumeration via WMI
- Drive letter management (Z→A ordering)
- Modern WPF UI with status updates
- PowerShell helper scripts

### Requirements

- Windows 10 Build 19041+ or Windows 11
- WSL2 with at least one Linux distribution installed
- Administrator privileges for mounting operations
