# AGENTS.md - LiMount

## What

LiMount mounts Linux partitions into WSL2 and exposes them in Windows through mode-based access:
- `NetworkLocation` (default)
- `DriveLetterLegacy` (explicit legacy mode)
- `None` (mount-only, no Windows integration)

## Structure

- **LiMount.Core** - Shared business logic (services, orchestrators, models)
- **LiMount.WinUI** - WinUI 3 UI (.NET 10, Windows 11)
- **LiMount.Tests** - Unit and integration tests
- **LiMount.UITests** - Deterministic UI automation tests (FlaUI)
- **scripts/** - PowerShell scripts for mount/unmount, Windows access integration, and HIL test runners

## Default Workflows

### 1) Fast development loop

```powershell
dotnet restore
dotnet build
dotnet test LiMount.Tests
dotnet run --project LiMount.WinUI
```

Use this for most feature work and bug fixes.

### 2) Deterministic UI test workflow

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

Full UI screenshot batch (main states + setup wizard + dropdowns):

```powershell
$env:LIMOUNT_RUN_UI_TESTS='1'
$env:LIMOUNT_CAPTURE_SCREENSHOT_BATCH='1'
dotnet test LiMount.UITests --filter "FullyQualifiedName~ScreenshotBatchUiTests"
Remove-Item Env:\LIMOUNT_RUN_UI_TESTS
Remove-Item Env:\LIMOUNT_CAPTURE_SCREENSHOT_BATCH
```

Batch output location:

- `screenshots\ui-batch\<yyyyMMdd-HHmmss>\`
- Deterministic filenames `01-main-initial.png` through `12-main-after-wizard.png`
- In default `NetworkLocation` mode, drive-letter picker is hidden; screenshot `04-main-drive-letter-dropdown-open.png` is still generated to keep deterministic naming.

### 3) Hardware-in-loop (real WSL + disk) regression workflow

Single expected-failure validation (for unsupported XFS feature cases):

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\run-hil-mount-test.ps1 -DiskIndex 1 -Partition 2 -ExpectXfsUnsupported
```

Drive-level end-to-end validation (expected failure partition + required success mount/unmount on another partition):

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\run-hil-mount-test.ps1 -DiskIndex 1 -VerifyDriveEndToEnd -FailurePartition 2
```

Runner behavior:

- Sets `LIMOUNT_REQUIRE_HIL=1` so the integration scenario fails (instead of silently skipping) when prerequisites are missing.
- Uses elevated-script path validation by default (`LIMOUNT_SKIP_SCRIPT_ELEVATION=0`).
- Use `-SkipScriptElevation` only in controlled environments where non-elevated `wsl --mount` is explicitly allowed.

Only run HIL workflows on machines with admin access and explicitly chosen non-system test disks.

### 4) Release packaging workflow

```powershell
.\build-release.ps1
```

Published output: `LiMount.WinUI\bin\Release\net10.0-windows10.0.26100.0\win-x64\publish\LiMount.WinUI.exe`  
Canonical pickup artifact: `bin\LiMount.WinUI.exe`

## Command Reference

- `dotnet build` - Build solution (default config)
- `dotnet build --configuration Release` - Build release configuration
- `dotnet test LiMount.Tests` - Run unit/service/integration test project
- `dotnet test LiMount.Tests --collect:"XPlat Code Coverage"` - Run tests with coverage output
- `dotnet test LiMount.UITests` - Run deterministic WinUI automation tests
- `dotnet run --project LiMount.WinUI` - Launch app for local testing
- `.\build-release.ps1` - Clean, restore, publish, and copy release artifact
- `powershell -ExecutionPolicy Bypass -File .\scripts\run-hil-mount-test.ps1 -DiskIndex <N> -Partition <P> -ExpectXfsUnsupported` - Single HIL validation
- `powershell -ExecutionPolicy Bypass -File .\scripts\run-hil-mount-test.ps1 -DiskIndex <N> -VerifyDriveEndToEnd -FailurePartition <P>` - HIL end-to-end validation
- `wsl --status` - Validate WSL availability before mount debugging
- `wsl --list --verbose` - Check distro/running state for troubleshooting

## Key Patterns

- Services injected via DI (interfaces in `LiMount.Core/Interfaces/`)
- Config via `IOptions<LiMountConfiguration>` from `appsettings.json`
- User defaults via `%LocalAppData%\LiMount\settings.json` (`AccessMode` defaults to `NetworkLocation`)
- Mount state tracked by `IMountStateService` (singleton), including access metadata
- Orchestrators coordinate workflows and validation
- `IWindowsAccessService` owns Windows-side integration routing (`NetworkLocation` / `DriveLetterLegacy` / `None`)
- Result objects (`MountAndMapResult`, `UnmountAndUnmapResult`) are mode-aware and persisted to history/state

## Logs

`%LocalAppData%\LiMount\logs\`

## Detailed Docs

For specialized topics, see `agent_docs/`:

- `aot-trimming.md` - AOT/trimming constraints
