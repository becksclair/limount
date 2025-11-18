---
trigger: always_on
---

# AGENTS.md
## Guidance for AI Agents on LiMount

### 🎯 Core Directives
1.  **Read `CLAUDE.md` First**: Contains architecture, patterns, and constraints.
2.  **"Ultrathink" Process**:
    *   **Analyze**: Understand intent, fit with architecture, check "Lessons Learned".
    *   **Design**: Define interfaces, config, state persistence, and testability *before* coding.
    *   **TDD**: Write failing tests first (Red -> Green -> Refactor). Mock dependencies.
    *   **Implement**: Minimal code to pass tests.
    *   **Refactor**: Clean up, register in DI, add docs.
    *   **Commit**: Small, incremental commits.

### ❌ Strict Prohibitions (NEVER)
*   **Hardcoding**: No timeouts/retries. Use `IOptions<LiMountConfiguration>`.
*   **`new` Services**: Always use Dependency Injection (constructor injection).
*   **Logic in ViewModels**: Move business logic to Services/Orchestrators.
*   **Direct `MessageBox`**: Use `IDialogService`.
*   **ViewModel State**: Use `IMountStateService` for persistence.
*   **Swallowing Exceptions**: Always log with context.
*   **Scattered Validation**: Validate *once* in the Orchestrator.

### 🧪 TDD Methodology
*   **Cycle**: Write failing test -> Minimal implementation -> Refactor.
*   **Order**: Happy path -> Validation -> Edge cases -> Errors.
*   **Mocking**: Mock all dependencies (e.g., `IScriptExecutor`, `IMountStateService`).
*   **Linux Constraint**: You are on Linux, app is Windows. Write tests anyway; mock Windows APIs. Document integration tests.

### 🛠️ Workflow & Problem Solving
1.  **Explore**: Use `Grep`/`Glob` to find similar patterns/services.
2.  **Design**: Plan interface, config, and state.
3.  **Test (Red)**: Create test file, write failing tests.
4.  **Implement (Green)**: Create interface/service, update config, pass tests.
5.  **Refactor**: Optimize, register in `App.xaml.cs`.
6.  **Integrate**: Connect to UI/ViewModels.
7.  **Document**: XML comments, update `CLAUDE.md` if needed.
8.  **Commit**: Atomic commits (feat, fix, refactor).

### ⚠️ Common Mistakes to Avoid
*   **Test-Last**: Writing code before tests.
*   **Hardcoded Config**: Using `const` instead of `appsettings.json`.
*   **Transient State**: Storing operational state in ViewModels (lost on restart).
*   **Untestable UI**: Using `MessageBox.Show` directly.
*   **Duplicate Validation**: Validating in ViewModel, Orchestrator, *and* Executor.
*   **Silent Failures**: Empty catch blocks.
*   **Massive Commits**: Batching unrelated changes.

### 📚 Reference Patterns
*   **Service**: Interface + Implementation + DI Registration + Config Injection.
*   **Result Object**: Use `Result` or `MountAndMapResult` (Success/Failure/Error).
*   **Config**: Add to `LiMountConfiguration`, update `appsettings.json`, inject `IOptions<>`.
