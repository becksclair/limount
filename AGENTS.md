# AGENTS.md
## Guidance for AI Agents Working on LiMount

This file provides specific guidance for AI code assistants (like Claude, Copilot, etc.) working on the LiMount codebase.

---

## 🎯 Core Directives

### 1. **ALWAYS Read CLAUDE.md First**
Before making ANY code changes, read `/home/user/limount/CLAUDE.md` completely. It contains:
- Project architecture and patterns
- Design principles you MUST follow
- Known limitations and constraints
- Common pitfalls to avoid

### 2. **Think Before Acting - "Ultrathink"**
When asked to implement a feature:

**Step 1**: Analyze the Request
- What is the user really trying to accomplish?
- Does this fit the existing architecture?
- What similar patterns exist in the codebase?

**Step 2**: Plan the Implementation
- Which services/interfaces are involved?
- What new files need to be created?
- What existing files need modification?
- How will this integrate with DI?

**Step 3**: Consider Implications
- Does this need configuration? (appsettings.json)
- Does this need persistence? (IMountStateService or IMountHistoryService)
- Does this need testing? (always yes!)
- Are there security implications?

**Step 4**: Execute Incrementally
- Create interfaces first
- Implement one service at a time
- Test as you go
- Commit frequently

### 3. **Never Violate Architecture Principles**
These are **RULES**, not suggestions:

❌ **NEVER** hardcode timeout/retry values (use IOptions<LiMountConfiguration>)
❌ **NEVER** use `new` for services (use DI)
❌ **NEVER** put business logic in ViewModels (use services/orchestrators)
❌ **NEVER** call MessageBox directly (use IDialogService)
❌ **NEVER** store mount state only in ViewModel (use IMountStateService)
❌ **NEVER** silently swallow exceptions without logging
❌ **NEVER** duplicate validation logic across layers

✅ **ALWAYS** inject dependencies via constructor
✅ **ALWAYS** create interfaces for new services
✅ **ALWAYS** validate parameters in orchestrators
✅ **ALWAYS** log errors with context
✅ **ALWAYS** return Result objects for operations
✅ **ALWAYS** use async/await for I/O

---

## 🔍 Problem-Solving Methodology

### When Adding a New Feature

```
1. EXPLORE: What exists?
   - Search for similar features: `Grep` tool
   - Find related services: `Glob` tool
   - Read existing implementations: `Read` tool

2. DESIGN: How should it work?
   - Interface: What contract is needed?
   - Implementation: Where does logic live?
   - Integration: How to wire into DI?
   - Configuration: What should be tunable?

3. IMPLEMENT: Build it right
   - Create interface in `LiMount.Core/Interfaces/`
   - Implement service in `LiMount.Core/Services/`
   - Add model if needed in `LiMount.Core/Models/`
   - Add configuration in `LiMount.Core/Configuration/` if needed
   - Register in `App.xaml.cs` DI
   - Update `appsettings.json` if needed

4. TEST: Verify it works
   - Create unit tests (or document why you can't on Linux)
   - Test happy path
   - Test error cases
   - Test edge cases

5. DOCUMENT: Help future developers
   - Add XML comments
   - Update CLAUDE.md if adding patterns
   - Update TODO.md if completing tasks
```

### When Fixing a Bug

```
1. REPRODUCE: Understand the problem
   - What's the expected behavior?
   - What's the actual behavior?
   - Under what conditions does it fail?

2. LOCATE: Find the root cause
   - Use `Grep` to find relevant code
   - Read through the call stack
   - Check logs if available
   - Look for similar reported issues

3. FIX: Address the root cause
   - Don't just patch symptoms
   - Consider if fix should be in orchestrator vs executor
   - Add validation if missing
   - Add error handling if missing

4. PREVENT: Stop it from happening again
   - Add validation to prevent invalid inputs
   - Add logging to make debugging easier
   - Add tests to catch regressions
   - Document gotchas in CLAUDE.md
```

### When Refactoring

```
1. JUSTIFY: Why refactor?
   - Code duplication?
   - Violates architecture principles?
   - Hard to test?
   - Hard to maintain?

2. PLAN: What's the end state?
   - What design pattern solves this?
   - What interfaces are needed?
   - What's the migration path?
   - Can it be done incrementally?

3. EXECUTE: Refactor safely
   - Create new interfaces/abstractions first
   - Implement new code alongside old
   - Migrate callers incrementally
   - Remove old code last
   - Commit at each stable point

4. VERIFY: Nothing broke
   - Run all tests
   - Check that DI still resolves
   - Verify UI still works
   - Review code for missed updates
```

---

## 🛠️ Tool Usage Guide

### When to Use Each Tool

**Glob**: Finding files by pattern
```
Use: Need to find all services, all models, all interfaces
Example: Glob pattern="**/*Service.cs"
```

**Grep**: Searching code content
```
Use: Need to find where something is used, defined, or referenced
Example: Grep pattern="IMountOrchestrator" output_mode="files_with_matches"
```

**Read**: Reading file contents
```
Use: Need to understand implementation details
Always read before editing!
```

**Edit**: Making surgical changes
```
Use: Changing specific code sections
Must read file first
```

**Write**: Creating new files
```
Use: Adding new services, interfaces, models
Prefer Edit for existing files
```

**Bash**: Running commands
```
Use: Git operations, checking file structure
NOT for reading code (use Read tool)
```

### Search Strategy

**Finding Similar Code**:
```bash
# 1. Find files with similar names
Glob pattern="**/Mount*.cs"

# 2. Find files using similar interfaces
Grep pattern="IMountOrchestrator" output_mode="files_with_matches"

# 3. Read the implementations
Read file_path="/home/user/limount/LiMount.Core/Services/MountOrchestrator.cs"
```

**Understanding a Feature**:
```bash
# 1. Find the interface
Glob pattern="**/IMountStateService.cs"

# 2. Find the implementation
Grep pattern="class.*:.*IMountStateService"

# 3. Find where it's used
Grep pattern="IMountStateService" -C=3

# 4. Find where it's registered in DI
Grep pattern="AddSingleton<IMountStateService" path="LiMount.App/App.xaml.cs"
```

---

## 📚 Common Patterns & Templates

### Creating a New Service

```csharp
// 1. Interface in LiMount.Core/Interfaces/IMyService.cs
namespace LiMount.Core.Interfaces;

public interface IMyService
{
    Task<Result> DoSomethingAsync(string param);
}

// 2. Implementation in LiMount.Core/Services/MyService.cs
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

[SupportedOSPlatform("windows")]
public class MyService : IMyService
{
    private readonly ILogger<MyService> _logger;
    private readonly LiMountConfiguration _config;

    public MyService(
        ILogger<MyService> logger,
        IOptions<LiMountConfiguration> config)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _config = config.Value;
    }

    public async Task<Result> DoSomethingAsync(string param)
    {
        // Validate
        if (string.IsNullOrWhiteSpace(param))
        {
            _logger.LogWarning("Invalid parameter provided");
            return Result.Failure("Parameter cannot be empty");
        }

        try
        {
            // Do work
            _logger.LogInformation("Doing something with {Param}", param);

            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to do something");
            return Result.Failure(ex.Message);
        }
    }
}

// 3. Register in App.xaml.cs
services.AddSingleton<IMyService, MyService>();
```

### Adding Configuration

```csharp
// 1. Add to LiMount.Core/Configuration/LiMountConfiguration.cs
public class MyFeatureConfig
{
    public int MaxRetries { get; set; } = 3;
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
}

// Update root config
public class LiMountConfiguration
{
    // ... existing properties
    public MyFeatureConfig MyFeature { get; set; } = new();
}

// 2. Add to appsettings.json
{
  "LiMount": {
    "MyFeature": {
      "MaxRetries": 3,
      "Timeout": "00:00:30"
    }
  }
}

// 3. Use in service
public class MyService
{
    private readonly MyFeatureConfig _config;

    public MyService(IOptions<LiMountConfiguration> config)
    {
        _config = config.Value.MyFeature;
    }
}
```

### Creating a Result Object

```csharp
namespace LiMount.Core.Models;

public class MyOperationResult
{
    public bool Success { get; set; }
    public string? Data { get; set; }
    public string? ErrorMessage { get; set; }
    public string? FailedStep { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.Now;

    public static MyOperationResult CreateSuccess(string data)
    {
        return new MyOperationResult
        {
            Success = true,
            Data = data
        };
    }

    public static MyOperationResult CreateFailure(string errorMessage, string failedStep)
    {
        return new MyOperationResult
        {
            Success = false,
            ErrorMessage = errorMessage,
            FailedStep = failedStep
        };
    }
}
```

---

## ⚠️ Critical Reminders

### State Management
```
❌ WRONG:
public class MainViewModel
{
    private int? _currentMountedDiskIndex; // Lost on restart!
}

✅ RIGHT:
public class MainViewModel
{
    private readonly IMountStateService _stateService;

    public async Task<bool> IsDiskMountedAsync(int diskIndex)
    {
        return await _stateService.IsDiskMountedAsync(diskIndex);
    }
}
```

### Configuration
```
❌ WRONG:
private const int Timeout = 5; // Hardcoded!

✅ RIGHT:
private readonly LiMountConfiguration _config;
var timeout = _config.ScriptExecution.TempFilePollingTimeoutSeconds;
```

### Dialog Abstraction
```
❌ WRONG:
var result = MessageBox.Show("Confirm?", "Title", ...); // Untestable!

✅ RIGHT:
var confirmed = await _dialogService.ConfirmAsync("Confirm?", "Title");
```

### Validation
```
❌ WRONG:
// Validation scattered in executor, orchestrator, ViewModel

✅ RIGHT:
// Validation once in orchestrator
if (diskIndex < 0)
{
    return Result.CreateFailure("Disk index must be non-negative", "validation");
}
```

---

## 🧪 Testing Considerations

### This is a Windows-Only Application

**Reality Check**: You're running on Linux, the app requires Windows.

**What you CAN do**:
- Create test project structure
- Write unit tests with mocks
- Test non-Windows-specific logic
- Document test scenarios

**What you CAN'T do**:
- Run the actual WPF application
- Test PowerShell script execution
- Test WMI queries
- Test actual mount/unmount operations

**Approach**:
```
1. Create LiMount.Tests project
2. Add xUnit, Moq, FluentAssertions
3. Write tests for:
   - Models (FromDictionary methods)
   - Validation logic
   - Configuration parsing
   - Result object creation
4. Mock interfaces for:
   - IScriptExecutor
   - IDiskEnumerationService
   - IMountStateService
5. Document integration tests that need Windows
```

---

## 📊 Decision Framework

### Should This Be Configurable?

**YES** if:
- Might need tuning for different environments
- Could vary by user preference
- Is a timeout/retry/limit value
- Affects performance or behavior

**NO** if:
- Is fundamental to the application
- Would never change (e.g., Windows platform)
- Is a UX string/label
- Increases complexity without benefit

### Should This Be a Service?

**YES** if:
- Has dependencies
- Needs to be mocked for testing
- Manages state or resources
- Encapsulates complex logic
- Will be reused across components

**NO** if:
- Is a simple data model (use class)
- Is a pure utility function (use static)
- Has no dependencies
- Is UI-specific (use ViewModel)

### Should This Be an Orchestrator?

**YES** if:
- Coordinates multiple services
- Has multi-step workflow with error handling
- Reports progress during operation
- Implements retry logic
- Combines results from multiple operations

**NO** if:
- Just executes one operation
- No coordination needed
- Better as part of existing orchestrator

---

## 🚀 Quick Start Checklist

Before implementing ANY feature:

- [ ] Read CLAUDE.md completely
- [ ] Understand the existing architecture
- [ ] Search for similar implementations
- [ ] Plan the interfaces needed
- [ ] Consider configuration needs
- [ ] Think about state management
- [ ] Design for testability
- [ ] Plan incremental commits
- [ ] Document as you go

---

## 💡 Pro Tips for AI Agents

1. **Use TODO.md as Your Guide**: Check what's planned before suggesting new work

2. **Commit Incrementally**: Don't wait until everything is perfect
   ```bash
   git commit -m "feat: add IMountStateService interface"
   git commit -m "feat: implement MountStateService"
   git commit -m "feat: integrate MountStateService in ViewModel"
   ```

3. **Read Before Writing**: Always use `Read` tool before `Edit` or `Write`

4. **Search Broadly**: Use `Grep` with broad patterns first, then narrow down

5. **Test Your Assumptions**: If unsure how something works, read the code

6. **Ask for Clarification**: If user request is ambiguous, ask questions first

7. **Think About Migration**: When refactoring, plan how existing code will migrate

8. **Document Decisions**: Update CLAUDE.md when adding new patterns

9. **Respect the Architecture**: When in doubt, follow existing patterns

10. **Be Incremental**: Small, working changes > big, broken changes

---

## 📖 Required Reading

Before working on this codebase, read these in order:

1. **This file (AGENTS.md)**: You're here!
2. **CLAUDE.md**: Architecture, patterns, principles
3. **TODO.md**: Planned work, current status
4. **README.md**: User-facing documentation

---

**Remember**: This codebase has learned from experience. The patterns exist for good reasons. Follow them, and you'll avoid the pitfalls we've already encountered.

---

**Version**: 1.0
**Last Updated**: 2025-11-18
**For**: AI Code Assistants working on LiMount
