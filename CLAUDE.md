# CLAUDE.md

## Project Overview
**LiMount** is a Windows WPF app (.NET 8) that mounts Linux partitions into WSL2 and maps them as Windows drives.
- **Tech**: .NET 8, WPF, CommunityToolkit.Mvvm, System.Management (WMI), Serilog.
- **Platform**: Windows-only.

## Build & Run
```bash
dotnet restore
dotnet build
dotnet run --project LiMount.App
# Tests
dotnet test LiMount.Tests
```

## Architecture
- **LiMount.Core**: Business logic, WMI, PowerShell interactions.
  - **Services**: `DiskEnumerationService`, `DriveLetterService`, `ScriptExecutor`.
  - **Orchestrators**: `MountOrchestrator`, `UnmountOrchestrator` (Workflow & Validation).
- **LiMount.App**: WPF UI, MVVM pattern.
- **Scripts**: PowerShell scripts in `scripts/` for WSL operations.

## Core Directives
1. **Dependency Injection**: Always inject interfaces. Never use `new` for services.
   - Services: Singleton.
   - Orchestrators/ViewModels: Transient.
2. **Configuration**: All tunable values (timeouts, retries) MUST go in `appsettings.json` via `IOptions<T>`. **No hardcoded constants.**
3. **State Management**: Use `IMountStateService` (Singleton) for active mounts. **Never** store state in ViewModels.
4. **Validation**: Validate centrally in Orchestrators. Return `Result` objects (Success/Failure), do not throw exceptions for validation.
5. **Async**: Use `async/await` for all I/O. Keep UI responsive.
6. **Error Handling**: Use typed Result objects (`MountResult`, `MappingResult`). Log all exceptions.

## Development Workflow
1. **Design First**: Define Interface -> Config -> Model -> Test.
2. **TDD**: Write failing tests (Red) -> Implement (Green) -> Refactor.
   - Mock dependencies (Moq).
   - Test business logic in `LiMount.Tests`.
3. **Commits**: Small, atomic commits (feat, fix, refactor).

## PowerShell Execution
- **Elevated**: Mount/Unmount. Uses `runas`, writes output to temp files.
- **Non-Elevated**: Map/Unmap. Captures stdout directly.
- **Output**: Scripts use `key=value` format.

## Key Services

| Service | Purpose | Lifetime |
|---------|---------|----------|
| `IMountStateService` | Track active mounts (Persisted) | Singleton |
| `IDiskEnumerationService` | Find disks (WMI) | Singleton |
| `IScriptExecutor` | Run PowerShell scripts | Singleton |
| `IMountOrchestrator` | Mount workflow & validation | Transient |
| `IDialogService` | Show UI dialogs | Singleton |

## Troubleshooting
- **Logs**: `%LocalAppData%\LiMount\logs\`
- **Mount Fails**: Check `ScriptExecution.TempFilePollingTimeoutSeconds` in config.
- **State Lost**: Ensure `IMountStateService` is used, not ViewModel properties.
