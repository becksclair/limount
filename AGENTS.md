# AGENTS.md - LiMount

## What

LiMount mounts Linux partitions into WSL2 and maps them as Windows drive letters.

## Structure

- **LiMount.Core** - Shared business logic (services, orchestrators, models)
- **LiMount.WinUI** - WinUI 3 UI (.NET 10, Windows 11)
- **LiMount.Tests** - Unit and integration tests
- **scripts/** - PowerShell scripts for mount/unmount operations

## Build & Test

```bash
dotnet build
dotnet test LiMount.Tests
dotnet run --project LiMount.WinUI
```

## Key Patterns

- Services injected via DI (interfaces in `LiMount.Core/Interfaces/`)
- Config via `IOptions<LiMountConfiguration>` from `appsettings.json`
- Mount state tracked by `IMountStateService` (singleton)
- Orchestrators coordinate workflows and validation
- Result objects (`MountAndMapResult`, `UnmountAndUnmapResult`) for outcomes

## Logs

`%LocalAppData%\LiMount\logs\`

## Detailed Docs

For specialized topics, see `agent_docs/`:
- `aot-trimming.md` - AOT/trimming constraints

