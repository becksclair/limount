# AGENTS.md - LiMount AI Agent Guide

## Project Overview

**LiMount**: Windows WPF app (.NET 8) that mounts Linux partitions into WSL2 and maps them as Windows drives.
- **Tech**: .NET 8, WPF, CommunityToolkit.Mvvm, WMI, Serilog
- **Structure**: `LiMount.Core` (services, orchestrators), `LiMount.App` (WPF UI), `scripts/` (PowerShell)

## Build & Test

```bash
dotnet restore && dotnet build
dotnet run --project LiMount.App
dotnet test LiMount.Tests
```

## 🎯 Core Directives

### Ultrathink Process

1. **Analyze**: Understand intent, check architecture fit
2. **Design**: Define interfaces, config, state persistence *before* coding
3. **TDD**: Failing tests → Minimal implementation → Refactor
4. **Implement**: Minimal code to pass tests
5. **Commit**: Small, atomic commits (feat/fix/refactor)

### Architecture Rules

- **DI**: Always inject interfaces. Never `new` for services
  - Services/State: Singleton | Orchestrators/ViewModels: Transient
- **Config**: All tunables in `appsettings.json` via `IOptions<LiMountConfiguration>`
- **State**: Use `IMountStateService` (Singleton) for active mounts
- **Validation**: Centrally in Orchestrators, return `Result` objects
- **Async**: `async/await` for all I/O

## ❌ Strict Prohibitions

- **Hardcoding**: No timeouts/retries—use `IOptions<LiMountConfiguration>`
- **`new` Services**: Always use DI (constructor injection)
- **Logic in ViewModels**: Move to Services/Orchestrators
- **Direct `MessageBox`**: Use `IDialogService`
- **ViewModel State**: Use `IMountStateService` for persistence
- **Swallowing Exceptions**: Always log with context
- **Scattered Validation**: Validate *once* in Orchestrator

## 🧪 TDD Methodology

- **Cycle**: Red → Green → Refactor
- **Order**: Happy path → Validation → Edge cases → Errors
- **Mocking**: Mock all deps (`IScriptExecutor`, `IMountStateService`, etc.)
- **Linux Dev**: Mock Windows APIs, document integration tests

## 🛠️ Workflow

1. **Explore**: Grep/Glob for similar patterns
2. **Design**: Interface → Config → Model
3. **Test (Red)**: Write failing tests
4. **Implement (Green)**: Create service, pass tests
5. **Refactor**: Register in `App.xaml.cs`, add XML docs
6. **Integrate**: Connect to UI/ViewModels
7. **Commit**: Atomic commits

## Key Services

| Service | Purpose | Lifetime |
|---------|---------|----------|
| `IMountStateService` | Track active mounts (persisted) | Singleton |
| `IDiskEnumerationService` | Find disks (WMI) | Singleton |
| `IScriptExecutor` | Run PowerShell scripts | Singleton |
| `IMountOrchestrator` | Mount workflow & validation | Transient |
| `IDialogService` | Show UI dialogs | Singleton |

## PowerShell Execution

- **Elevated**: Mount/Unmount via `runas`, output to temp files
- **Non-Elevated**: Map/Unmap, captures stdout directly
- **Output Format**: `key=value`

## ⚠️ Common Mistakes

- **Test-Last**: Writing code before tests
- **Hardcoded Config**: Using `const` instead of `appsettings.json`
- **Transient State**: Storing state in ViewModels
- **Silent Failures**: Empty catch blocks
- **Massive Commits**: Batching unrelated changes

## 📚 Reference Patterns

- **Service**: Interface + Implementation + DI Registration + Config Injection
- **Result Object**: `Result` or `MountAndMapResult` (Success/Failure/Error)
- **Config**: Add to `LiMountConfiguration`, update `appsettings.json`, inject `IOptions<>`

## Troubleshooting

- **Logs**: `%LocalAppData%\LiMount\logs\`
- **Mount Fails**: Check `ScriptExecution.TempFilePollingTimeoutSeconds`
- **State Lost**: Ensure `IMountStateService` is used, not ViewModel properties
