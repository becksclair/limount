# Changelog

All notable changes to LiMount will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- **Mount functionality**: Mount Linux disk partitions into WSL2 with `wsl --mount`
- **Drive mapping**: Map WSL UNC paths to Windows drive letters
- **Unmount functionality**: Safely unmount disks and unmap drive letters
- **Environment validation**: Startup checks for WSL, distros, and Windows version
- **Mount state tracking**: Persistent JSON storage of active mounts
- **Mount history**: Track all mount/unmount operations with timestamps
- **History window**: View past mount operations
- **Configuration**: `appsettings.json` with configurable timeouts and retries
- **Logging**: Serilog file logging to `%LocalAppData%\LiMount\logs\`

### Architecture

- Clean architecture with DI, interfaces, and orchestration layer
- MVVM pattern with CommunityToolkit.Mvvm
- Separated concerns: Services → Orchestrators → ViewModels → Views
- PowerShell scripts for elevated (mount/unmount) and non-elevated (map/unmap) operations

### Technical

- .NET 8 WPF application (Windows-only)
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
