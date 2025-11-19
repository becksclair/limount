# ADR-002: Use Orchestrator Pattern for Multi-Step Workflows

**Status**: Accepted

**Date**: 2025-11-18

**Deciders**: Development Team

**Context**: Mount/unmount operations involve multiple coordinated steps across different services with progress reporting and error recovery.

---

## Context and Problem Statement

Mounting a disk and mapping it to a drive letter involves multiple steps:
1. Validate parameters
2. Check if disk is already mounted
3. Execute WSL mount script (elevated PowerShell)
4. Verify mount success
5. Wait for UNC path to become accessible (with retry)
6. Map UNC path to Windows drive letter
7. Record mount in state service
8. Log to history service
9. Report progress to UI throughout

Similar complexity exists for unmount operations. We need a pattern that:
- Coordinates multiple services
- Handles errors at each step gracefully
- Reports progress to UI
- Implements retry logic
- Doesn't bloat individual services with workflow logic

## Decision Drivers

- **Separation of Concerns**: ScriptExecutor should execute scripts, not orchestrate multi-step flows
- **Testability**: Should be able to test workflow logic by mocking services
- **Progress Reporting**: UI needs updates throughout the operation
- **Error Recovery**: Need sophisticated retry and error handling
- **Maintainability**: Workflow logic should be in one place, not scattered

## Considered Options

### Option 1: Put Workflow in Service (Fat Service)

```csharp
public class MountService
{
    public async Task<bool> MountAndMapAsync(int diskIndex, int partition, char driveLetter, IProgress<string> progress)
    {
        // Validation
        // Check state
        // Execute mount
        // Retry UNC accessibility
        // Map drive
        // Update state
        // Log history
        // All in one giant method
    }
}
```

**Pros**:
- Simple (one service does everything)
- Easy to find all logic in one place

**Cons**:
- Violates Single Responsibility Principle
- Hard to test (giant method with many dependencies)
- Can't reuse pieces of workflow
- Service becomes bloated
- Hard to maintain (one class does too much)

### Option 2: Put Workflow in ViewModel

```csharp
public class MainViewModel
{
    public async Task MountAsync()
    {
        // Validate
        var mountResult = await _scriptExecutor.MountAsync(...);
        if (!mountResult.Success) { ... }

        // Wait for UNC with retry
        for (int i = 0; i < 5; i++)
        {
            if (Directory.Exists(uncPath)) break;
            await Task.Delay(500);
        }

        // Map drive
        var mapResult = await _scriptExecutor.MapDriveAsync(...);

        // Update state
        await _stateService.RegisterMountAsync(...);

        // Log history
        await _historyService.LogAsync(...);
    }
}
```

**Pros**:
- No new classes needed
- Logic is close to UI

**Cons**:
- ViewModel becomes bloated with business logic
- Hard to test (need to test VM instead of pure logic)
- Can't reuse workflow outside this ViewModel
- Violates MVVM pattern (ViewModels should be thin)
- Progress reporting is tangled with workflow

### Option 3: Orchestrator Pattern (Separate Coordinator)

```csharp
public class MountOrchestrator : IMountOrchestrator
{
    private readonly IScriptExecutor _executor;
    private readonly IMountStateService _stateService;
    private readonly IMountHistoryService _historyService;

    public async Task<MountAndMapResult> MountAndMapAsync(
        int diskIndex,
        int partition,
        char driveLetter,
        IProgress<string>? progress = null)
    {
        // Validate parameters
        progress?.Report("Validating parameters...");
        var validationError = ValidateParameters(...);
        if (validationError != null) return validationError;

        // Check existing mounts
        progress?.Report("Checking existing mounts...");
        // ...

        // Execute mount
        progress?.Report("Mounting disk in WSL...");
        var mountResult = await _executor.ExecuteMountScriptAsync(...);
        if (!mountResult.Success) return MountAndMapResult.FromMountFailure(mountResult);

        // Wait for UNC accessibility with retry
        progress?.Report("Waiting for network path...");
        var uncPath = await WaitForUncAccessibilityAsync(mountResult.UncPath, progress);
        if (uncPath == null) return MountAndMapResult.CreateFailure(..., "unc-accessibility");

        // Map to drive letter
        progress?.Report($"Mapping to drive {driveLetter}:...");
        var mapResult = await _executor.ExecuteMapDriveScriptAsync(...);
        if (!mapResult.Success) return MountAndMapResult.FromMappingFailure(mapResult);

        // Update state
        await _stateService.RegisterMountAsync(...);

        // Log history
        await _historyService.LogMountAsync(...);

        progress?.Report("Mount complete!");
        return MountAndMapResult.CreateSuccess(...);
    }

    private async Task<string?> WaitForUncAccessibilityAsync(string uncPath, IProgress<string>? progress)
    {
        // Retry logic here
    }

    private MountAndMapResult? ValidateParameters(...)
    {
        // Validation logic here
    }
}
```

**Pros**:
- Single Responsibility: coordinates workflow, delegates execution
- Testable: mock all dependencies (executor, state, history)
- Reusable: can be called from any ViewModel or service
- Clean separation: execution vs. coordination
- Progress reporting built in
- Easy to add retry logic, error recovery

**Cons**:
- More classes (one per workflow type)
- Indirection (call orchestrator instead of service directly)

## Decision Outcome

**Chosen option**: Option 3: Orchestrator Pattern

**Rationale**:
Multi-step workflows with error handling, retry logic, and progress reporting need coordination separate from execution. Putting this in ScriptExecutor violates SRP and bloats the executor. Putting it in ViewModel bloats the VM and makes it untestable.

Orchestrators provide:
- Clean separation: execution (executor) vs. coordination (orchestrator)
- Testability: mock all dependencies
- Reusability: any caller can use orchestrator
- Maintainability: workflow logic in one place

The cost of additional classes is justified by the clarity and testability benefits.

### Consequences

**Positive**:
- Workflow logic is testable (mock dependencies)
- ScriptExecutor stays focused on script execution
- ViewModels stay thin (just call orchestrator)
- Easy to add new workflows (create new orchestrator)
- Progress reporting is consistent

**Negative**:
- More classes to maintain
- Indirection (caller → orchestrator → executor)
- Need to register orchestrators in DI

**Mitigation**:
- Document orchestrator pattern in CLAUDE.md
- Provide templates in AGENTS.md
- Use transient lifetime (no state between calls)

**Risks**:
- Risk: Orchestrators become bloated "god objects"
- Mitigation: Each orchestrator handles ONE workflow (mount or unmount, not both)

## Implementation Notes

Orchestrators follow this pattern:

```csharp
public interface IMountOrchestrator
{
    Task<MountAndMapResult> MountAndMapAsync(
        int diskIndex,
        int partition,
        char driveLetter,
        IProgress<string>? progress = null);
}

[SupportedOSPlatform("windows")]
public class MountOrchestrator : IMountOrchestrator
{
    // Injected dependencies
    private readonly IScriptExecutor _executor;
    private readonly IMountStateService _stateService;
    private readonly IMountHistoryService _historyService;
    private readonly ILogger<MountOrchestrator> _logger;
    private readonly LiMountConfiguration _config;

    public MountOrchestrator(
        IScriptExecutor executor,
        IMountStateService stateService,
        IMountHistoryService historyService,
        IOptions<LiMountConfiguration> config,
        ILogger<MountOrchestrator> logger)
    {
        // Constructor injection
    }

    // Public workflow method
    public async Task<MountAndMapResult> MountAndMapAsync(...)
    {
        // Validate
        // Execute steps
        // Handle errors
        // Report progress
        // Return result
    }

    // Private helper methods for workflow steps
    private async Task<string?> WaitForUncAccessibilityAsync(...) { }
    private MountAndMapResult? ValidateParameters(...) { }
}
```

Registered as **Transient** in DI (no state between calls):
```csharp
services.AddTransient<IMountOrchestrator, MountOrchestrator>();
services.AddTransient<IUnmountOrchestrator, UnmountOrchestrator>();
```

Implemented in:
- `LiMount.Core/Interfaces/IMountOrchestrator.cs`
- `LiMount.Core/Services/MountOrchestrator.cs`
- `LiMount.Core/Interfaces/IUnmountOrchestrator.cs`
- `LiMount.Core/Services/UnmountOrchestrator.cs`

## Validation

Success criteria:
- ScriptExecutor has no workflow logic (only script execution)
- ViewModels just call orchestrator methods
- All workflow logic is testable with mocks
- Progress reporting works consistently

Metrics:
- MountOrchestrator: 350 lines (complex workflow)
- ScriptExecutor: 450 lines (just execution, no workflow)
- MainViewModel: 350 lines (thin, delegates to orchestrator)
- Test coverage: 85%+ on orchestrator validation logic

## Links

- Martin Fowler: [Service Layer](https://martinfowler.com/eaaCatalog/serviceLayer.html)
- CLAUDE.md: "Orchestrator Pattern" section
- AGENTS.md: "Should This Be an Orchestrator?" decision framework

---

## Metadata

**Related ADRs**:
- ADR-001 (Orchestrators return result objects)
- ADR-004 (Orchestrators use state service for persistence)

**Tags**: architecture, patterns, separation-of-concerns

**Review Date**: 2026-05-01
