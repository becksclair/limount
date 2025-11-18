# ADR-001: Use Result Objects Instead of Exceptions for Expected Failures

**Status**: Accepted

**Date**: 2025-11-18

**Deciders**: Development Team

**Context**: Mount/unmount operations commonly fail due to expected conditions (invalid input, WSL not running, disk busy, etc.)

---

## Context and Problem Statement

When performing mount/unmount operations, failures are common and expected:
- User selects invalid disk/partition
- WSL is not running
- Disk is already mounted
- Network share becomes unavailable
- PowerShell script fails gracefully

We need a consistent way to handle these expected failures that:
1. Doesn't pollute code with try-catch blocks
2. Forces callers to check for errors
3. Carries rich error context (which step failed, error message, timestamp)
4. Is easy to test without catching exceptions

## Decision Drivers

- **Testability**: Testing exception paths requires try-catch in tests
- **Clarity**: Expected failures (mount fails) are not exceptional circumstances
- **Context**: Need to know WHICH step of multi-step workflow failed
- **Caller Contract**: Want compiler to force error checking
- **Maintainability**: Consistent error handling pattern across codebase

## Considered Options

### Option 1: Traditional Exceptions

```csharp
public async Task MountAsync(int diskIndex, int partition, char driveLetter)
{
    if (diskIndex < 0)
        throw new ArgumentException("Disk index must be non-negative");

    var result = await ExecuteScript(...);
    if (result.ExitCode != 0)
        throw new MountException("Mount failed");
}
```

**Pros**:
- Standard C# idiom
- Framework support (exception filters, etc.)
- Stack traces for debugging

**Cons**:
- Expected failures aren't exceptional
- Expensive (stack unwinding)
- Try-catch everywhere clutters code
- Hard to test (need to catch exceptions)
- Doesn't carry rich context easily

### Option 2: Boolean Return with Out Parameters

```csharp
public async Task<bool> MountAsync(int diskIndex, int partition, char driveLetter, out string errorMessage)
{
    if (diskIndex < 0)
    {
        errorMessage = "Invalid disk index";
        return false;
    }
    errorMessage = null;
    return true;
}
```

**Pros**:
- Simple
- Cheap (no exceptions)

**Cons**:
- Can't use with async (out parameters not allowed)
- Doesn't scale to rich error context
- Easy to ignore return value
- Not idiomatic C#

### Option 3: Result Objects

```csharp
public async Task<MountResult> MountAsync(int diskIndex, int partition, char driveLetter)
{
    if (diskIndex < 0)
    {
        return MountResult.CreateFailure("Disk index must be non-negative", "validation");
    }

    var result = await ExecuteScript(...);
    if (!result.Success)
    {
        return MountResult.CreateFailure(result.ErrorMessage, "mount-script");
    }

    return MountResult.CreateSuccess(mountPath, uncPath);
}
```

**Pros**:
- Forces callers to check `result.Success`
- Carries rich context (failed step, error message, timestamp, etc.)
- Easy to test (no exception catching needed)
- Cheap (no stack unwinding)
- Chainable (can pass result up call stack)
- Explicit error handling

**Cons**:
- More verbose than exceptions
- Need to create result classes for each operation type
- Requires discipline to check success

## Decision Outcome

**Chosen option**: Option 3: Result Objects

**Rationale**:
Mount/unmount operations fail frequently for expected reasons (user error, environment issues). Using exceptions for these cases:
- Makes testing harder (need try-catch in every test)
- Is expensive (stack unwinding on every failure)
- Doesn't carry enough context (which step failed?)

Result objects are explicit, cheap, testable, and carry rich error context. The verbosity is acceptable given the benefits.

### Consequences

**Positive**:
- Tests are simpler (just check `result.Success`)
- Error context is rich (know which step failed)
- No expensive exception overhead
- Forces callers to handle errors explicitly

**Negative**:
- More boilerplate (need to create result classes)
- Requires discipline to check `.Success` before using result data
- Not standard C# idiom (some developers may be unfamiliar)

**Mitigation**:
- Created base result patterns in CLAUDE.md
- Added helper methods `CreateSuccess()` and `CreateFailure()`
- Document pattern in AGENTS.md for AI assistants

**Risks**:
- Risk: Caller forgets to check `.Success` and uses data anyway
- Mitigation: Use nullable reference types for success-only properties

## Implementation Notes

Result classes follow this pattern:

```csharp
public class MountResult
{
    public bool Success { get; set; }
    public string? DistroName { get; set; }       // Only set on success
    public string? MountPathLinux { get; set; }  // Only set on success
    public string? MountPathUNC { get; set; }    // Only set on success
    public string? ErrorMessage { get; set; }     // Only set on failure

    /// <summary>
    /// Creates a MountResult from PowerShell script output dictionary.
    /// Parses key=value pairs from script stdout/stderr.
    /// </summary>
    public static MountResult FromDictionary(Dictionary<string, string> values)
    {
        var success = KeyValueOutputParser.IsSuccess(values);

        return new MountResult
        {
            Success = success,
            DistroName = values.TryGetValue("DistroName", out var distro) ? distro : null,
            MountPathLinux = values.TryGetValue("MountPathLinux", out var linuxPath) ? linuxPath : null,
            MountPathUNC = values.TryGetValue("MountPathUNC", out var uncPath) ? uncPath : null,
            ErrorMessage = values.TryGetValue("ErrorMessage", out var error) ? error : null
        };
    }
}
```

This pattern parses PowerShell script output that produces key=value format (e.g., "STATUS=OK", "MountPathLinux=/mnt/wsl/PHYSICALDRIVE2p1").

**Concrete Properties Per Model**:
- **MountResult**: `DistroName`, `MountPathLinux`, `MountPathUNC`, `ErrorMessage`
- **UnmountResult**: `DiskIndex`, `ErrorMessage`
- **MappingResult**: `DriveLetter`, `TargetUNC`, `ErrorMessage`

**Note**: `MountAndMapResult` is the sole exception using the factory pattern (`CreateSuccess`/`CreateFailure`) rather than `FromDictionary` parsing, as it aggregates results from multiple operations rather than parsing direct script output.

Implemented in:
- `LiMount.Core/Models/MountResult.cs`
- `LiMount.Core/Models/MountAndMapResult.cs`
- `LiMount.Core/Models/UnmountResult.cs`
- `LiMount.Core/Models/MappingResult.cs`

## Validation

Success criteria:
- All orchestrator methods return result objects
- Test coverage includes error paths without try-catch
- Error messages are clear and actionable
- Failed step tracking enables debugging

Metrics (as of 2025-11-18):
- 25 tests written without exception handling
- Zero unhandled exceptions in production logs (target)
- Average test code reduction: ~30% (no try-catch boilerplate, estimated)

*Note: These metrics should be reviewed quarterly to maintain accuracy*

## Links

- Related Pattern: [Railway-Oriented Programming](https://fsharpforfunandprofit.com/rop/)
- CLAUDE.md: "Result Objects Over Exceptions" section
- AGENTS.md: "Creating a Result Object" template

---

## Metadata

**Related ADRs**: ADR-002 (Orchestrator Pattern uses result objects)

**Tags**: architecture, error-handling, testing

**Review Date**: 2026-05-01
