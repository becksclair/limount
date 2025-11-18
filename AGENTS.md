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
When asked to implement a feature, follow this enhanced process:

**Step 1**: Analyze the Request
- What is the user really trying to accomplish?
- Does this fit the existing architecture?
- What similar patterns exist in the codebase?
- **NEW**: What mistakes from CLAUDE.md "Lessons Learned" apply here?

**Step 2**: Design First (Before Any Code)
- What interfaces are needed?
- What configuration values should be tunable?
- What state needs to persist vs. what's transient UI state?
- How will this be tested? (Design for testability!)
- Which services/orchestrators coordinate the workflow?

**Step 3**: Write Tests FIRST (TDD)
- Write failing tests that define the contract
- Start with happy path, then edge cases
- Mock all dependencies
- Verify tests fail for the right reason

**Step 4**: Implement Minimally (Make Tests Pass)
- Write just enough code to pass tests
- Don't add features not covered by tests
- Keep it simple

**Step 5**: Refactor and Integrate
- Clean up implementation
- Register in DI
- Add XML documentation
- Update CLAUDE.md if new patterns emerge

**Step 6**: Commit Incrementally
- Commit after each stable point
- Small commits with clear messages
- Don't batch unrelated changes

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

## 🧪 Test-Driven Development (TDD) Methodology

### The RED-GREEN-REFACTOR Cycle

**CRITICAL**: Always write tests BEFORE implementation. This is non-negotiable.

#### Phase 1: RED (Write Failing Test)

```csharp
// Example: Adding validation to MountOrchestrator
[Fact]
public async Task MountOrchestrator_NegativeDiskIndex_ReturnsValidationError()
{
    // Arrange
    var mockExecutor = new Mock<IScriptExecutor>();
    var mockStateService = new Mock<IMountStateService>();
    var mockHistoryService = new Mock<IMountHistoryService>();
    var config = Options.Create(new LiMountConfiguration());

    var orchestrator = new MountOrchestrator(
        mockExecutor.Object,
        mockStateService.Object,
        mockHistoryService.Object,
        config,
        null);

    // Act
    var result = await orchestrator.MountAndMapAsync(-1, 1, 'Z');

    // Assert
    result.Success.Should().BeFalse();
    result.FailedStep.Should().Be("validation");
    result.ErrorMessage.Should().Contain("Disk index");
}
```

**Run the test** - It should FAIL because validation doesn't exist yet.

#### Phase 2: GREEN (Make Test Pass)

```csharp
public async Task<MountAndMapResult> MountAndMapAsync(
    int diskIndex,
    int partition,
    char driveLetter)
{
    // Add MINIMAL code to pass the test
    if (diskIndex < 0)
    {
        _logger.LogWarning("Invalid disk index: {DiskIndex}", diskIndex);
        return MountAndMapResult.CreateFailure(
            diskIndex,
            partition,
            "Disk index must be non-negative",
            "validation");
    }

    // ... rest of implementation
}
```

**Run the test** - It should PASS.

#### Phase 3: REFACTOR (Improve Code Quality)

```csharp
// Extract validation to separate method
private MountAndMapResult? ValidateParameters(int diskIndex, int partition, char driveLetter)
{
    if (diskIndex < 0)
    {
        _logger.LogWarning("Invalid disk index: {DiskIndex}", diskIndex);
        return MountAndMapResult.CreateFailure(
            diskIndex, partition,
            "Disk index must be non-negative",
            "validation");
    }

    if (partition < 1)
    {
        _logger.LogWarning("Invalid partition number: {Partition}", partition);
        return MountAndMapResult.CreateFailure(
            diskIndex, partition,
            "Partition number must be positive",
            "validation");
    }

    if (!char.IsLetter(driveLetter))
    {
        _logger.LogWarning("Invalid drive letter: {DriveLetter}", driveLetter);
        return MountAndMapResult.CreateFailure(
            diskIndex, partition,
            "Drive letter must be a letter",
            "validation");
    }

    return null; // Validation passed
}

public async Task<MountAndMapResult> MountAndMapAsync(...)
{
    var validationError = ValidateParameters(diskIndex, partition, driveLetter);
    if (validationError != null)
    {
        return validationError;
    }

    // ... rest of implementation
}
```

**Run ALL tests** - They should still pass after refactoring.

### TDD Best Practices for AI Agents

1. **Write Tests in This Order**:
   - ✅ Happy path (normal success case)
   - ✅ Validation errors (invalid inputs)
   - ✅ Edge cases (boundary conditions)
   - ✅ Error conditions (exceptions, failures)

2. **Always Mock Dependencies**:
   ```csharp
   // GOOD: Dependencies are mocked
   var mockExecutor = new Mock<IScriptExecutor>();
   mockExecutor.Setup(x => x.ExecuteMountScriptAsync(...))
              .ReturnsAsync(MountResult.CreateSuccess(...));

   // BAD: Using real services in unit tests
   var executor = new ScriptExecutor(...); // NO!
   ```

3. **One Assertion Per Test (Ideally)**:
   ```csharp
   // GOOD: Tests one thing
   [Fact]
   public async Task Mount_InvalidDiskIndex_ReturnsError()
   {
       var result = await orchestrator.MountAndMapAsync(-1, 1, 'Z');
       result.Success.Should().BeFalse();
   }

   // ACCEPTABLE: Related assertions about the same behavior
   [Fact]
   public async Task Mount_InvalidDiskIndex_ReturnsValidationError()
   {
       var result = await orchestrator.MountAndMapAsync(-1, 1, 'Z');
       result.Success.Should().BeFalse();
       result.FailedStep.Should().Be("validation");
       result.ErrorMessage.Should().Contain("Disk index");
   }

   // BAD: Testing multiple unrelated things
   [Fact]
   public async Task Mount_VariousScenarios()
   {
       // Tests 10 different things - split into separate tests!
   }
   ```

4. **Test Names Should Be Descriptive**:
   ```csharp
   // GOOD: Clear what's being tested
   [Fact]
   public async Task MountOrchestrator_NegativeDiskIndex_ReturnsValidationError()

   // BAD: Unclear what's being tested
   [Fact]
   public async Task TestMount()
   ```

5. **Use AAA Pattern (Arrange-Act-Assert)**:
   ```csharp
   [Fact]
   public async Task Example()
   {
       // Arrange - Set up test data and mocks
       var mock = new Mock<IService>();
       var sut = new SystemUnderTest(mock.Object);

       // Act - Execute the operation
       var result = await sut.DoSomethingAsync();

       // Assert - Verify the outcome
       result.Should().BeTrue();
   }
   ```

### When You Can't Run Tests (Linux Environment)

**Reality**: You're on Linux, this is a Windows-only WPF app.

**What to do**:
1. **Write the tests anyway** - They'll run on Windows
2. **Document test scenarios** - In comments if code won't compile
3. **Focus on testable logic** - Models, validation, parsing
4. **Mock Windows-specific APIs** - Test orchestration logic, not WMI/PowerShell

**Example of documenting tests you can't run**:
```csharp
// NOTE: This test requires Windows to execute
// Test Scenario: When DiskEnumerationService queries WMI, it should filter system disks
// Expected: System/Boot disks are excluded from results
//
// [Fact]
// public async Task DiskEnumerationService_GetCandidateDisks_ExcludesSystemDisks()
// {
//     // Would test this on Windows CI/CD
// }
```

---

## 🔍 Problem-Solving Methodology

### When Adding a New Feature

**IMPORTANT**: Follow this exact order. Don't skip steps!

```
1. EXPLORE: What exists?
   - Search for similar features: `Grep` tool
   - Find related services: `Glob` tool
   - Read existing implementations: `Read` tool
   - Review CLAUDE.md "Lessons Learned" section

2. DESIGN: Plan before coding
   - Interface: What contract is needed?
   - Configuration: What should be tunable? (appsettings.json)
   - State: What persists vs. what's transient? (Service vs. ViewModel)
   - Implementation: Where does logic live? (Service vs. Orchestrator)
   - Integration: How to wire into DI?

3. TEST-FIRST: Write failing tests (TDD RED)
   - Create test file in `LiMount.Tests/Services/`
   - Write test for interface contract
   - Write tests for validation rules
   - Write tests for error conditions
   - **Run tests** - They should FAIL (no implementation yet)

4. IMPLEMENT: Make tests pass (TDD GREEN)
   - Create interface in `LiMount.Core/Interfaces/`
   - Add configuration in `LiMount.Core/Configuration/` if needed
   - Update `appsettings.json` with default values if needed
   - Implement service in `LiMount.Core/Services/`
   - Add model if needed in `LiMount.Core/Models/`
   - Write MINIMAL code to pass tests
   - **Run tests** - They should PASS

5. REFACTOR: Clean up (TDD REFACTOR)
   - Extract helper methods
   - Improve naming
   - Add XML documentation
   - Register in `App.xaml.cs` DI
   - **Run tests** - Still passing?

6. INTEGRATE: Connect to UI/features
   - Inject into ViewModels or orchestrators
   - Wire up commands/bindings
   - Test manually in running app (on Windows)

7. DOCUMENT: Help future developers
   - Add XML comments on public members
   - Update CLAUDE.md if adding new patterns
   - Update TODO.md if completing tasks
   - Commit with clear message

8. COMMIT: Save your work
   - Commit after each stable point
   - Don't batch unrelated changes
   - Use conventional commit format (feat:, fix:, refactor:)
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

## ⚠️ Common Mistakes for AI Agents

Learn from our experience. These mistakes were made during LiMount development. DON'T REPEAT THEM!

### Mistake #1: Test-Last Development

**What AI agents often do**:
```
User: "Add feature X"
Agent: *immediately writes implementation*
Agent: *maybe adds tests at the end if reminded*
```

**Why it's wrong**:
- Tests feel like afterthought, are incomplete
- Code designed for implementation, not testability
- Bugs that TDD would catch slip through
- Hard to retrofit tests onto untestable code

**What you SHOULD do**:
1. Design interface
2. Write failing test
3. Implement minimal code to pass
4. Refactor
5. Repeat

**Red flag**: If you're writing implementation code before any tests exist, STOP!

### Mistake #2: Hardcoding Configuration Values

**What AI agents often do**:
```csharp
// "I'll make it configurable later"
private const int TIMEOUT_SECONDS = 5;
private const int MAX_RETRIES = 3;
```

**Why it's wrong**:
- "Later" never comes
- Can't tune for different environments without recompiling
- Users can't adjust behavior
- Testing becomes inflexible

**What you SHOULD do**:
```csharp
// Add to LiMountConfiguration FIRST
public class MyFeatureConfig
{
    public int TimeoutSeconds { get; set; } = 5;
    public int MaxRetries { get; set; } = 3;
}

// Then use in service
public MyService(IOptions<LiMountConfiguration> config)
{
    _timeout = config.Value.MyFeature.TimeoutSeconds;
}
```

**Red flag**: Any literal numbers in business logic code!

### Mistake #3: Storing State Only in ViewModel

**What AI agents often do**:
```csharp
public class MainViewModel
{
    private int? _currentMountedDisk; // Lost on app close!
    private char? _currentDriveLetter; // Lost on app close!
}
```

**Why it's wrong**:
- State lost when app closes
- Can't detect orphaned mounts on startup
- Multiple instances don't share state
- Can't reconcile with actual system state

**What you SHOULD do**:
```csharp
// Use persistent state service
var mount = await _mountStateService.GetMountForDiskAsync(diskIndex);

// State survives restarts, can be reconciled
```

**Red flag**: Properties in ViewModel that track operational state (not just UI state)!

### Mistake #4: Direct MessageBox Calls

**What AI agents often do**:
```csharp
var result = MessageBox.Show("Are you sure?", "Confirm", MessageBoxButton.YesNo);
if (result == MessageBoxResult.Yes)
{
    // Do something
}
```

**Why it's wrong**:
- ViewModel becomes completely untestable
- Can't mock dialogs in tests
- Tight coupling to UI framework
- Hard to automate or script

**What you SHOULD do**:
```csharp
// Inject IDialogService
private readonly IDialogService _dialogService;

// Use abstraction
var confirmed = await _dialogService.ConfirmAsync("Are you sure?", "Confirm");
if (confirmed)
{
    // Do something - TESTABLE!
}
```

**Red flag**: `MessageBox.Show` anywhere in ViewModel or service code!

### Mistake #5: Scattered Validation Logic

**What AI agents often do**:
```csharp
// Validation in ViewModel
if (SelectedDisk == null) return;

// Validation in Orchestrator
if (diskIndex < 0) return error;

// Validation in Executor
if (string.IsNullOrEmpty(distro)) throw new Exception();

// Validation in PowerShell script
if ($diskIndex -lt 0) { exit 1 }
```

**Why it's wrong**:
- Inconsistent error messages
- Duplicate logic
- Hard to maintain (change validation = change 4 places)
- Different layers have different validation rules

**What you SHOULD do**:
```csharp
// Validate ONCE in orchestrator (before calling executor)
public async Task<MountResult> MountAsync(...)
{
    // All validation here
    if (diskIndex < 0)
        return MountResult.Failure("Disk index must be non-negative");
    if (partition < 1)
        return MountResult.Failure("Partition must be positive");
    if (!ValidateDriveLetter(driveLetter))
        return MountResult.Failure("Invalid drive letter");

    // Executor trusts orchestrator validated everything
    return await _executor.ExecuteMountAsync(diskIndex, partition, driveLetter);
}
```

**Red flag**: Validation logic in multiple layers!

### Mistake #6: Silent Exception Swallowing

**What AI agents often do**:
```csharp
try
{
    await DoSomethingAsync();
}
catch (Exception)
{
    // Ignore - *poof* error disappears
}
```

**Why it's wrong**:
- Impossible to debug production issues
- User gets no feedback
- Errors propagate silently
- Log files are useless

**What you SHOULD do**:
```csharp
try
{
    await DoSomethingAsync();
}
catch (Exception ex)
{
    _logger.LogError(ex, "Failed to do something with {Context}", context);
    return Result.Failure($"Operation failed: {ex.Message}");
}
```

**Red flag**: Empty catch blocks or catch without logging!

### Mistake #7: Creating Services with `new`

**What AI agents often do**:
```csharp
public class MyViewModel
{
    private readonly IDiskService _diskService = new DiskService(); // NO!
}
```

**Why it's wrong**:
- Can't mock for testing
- Violates Dependency Injection principle
- Hard to swap implementations
- Tight coupling

**What you SHOULD do**:
```csharp
public class MyViewModel
{
    private readonly IDiskService _diskService;

    public MyViewModel(IDiskService diskService) // Injected
    {
        _diskService = diskService ?? throw new ArgumentNullException(nameof(diskService));
    }
}
```

**Red flag**: `new SomeService()` anywhere except DI registration!

### Mistake #8: Massive "Do Everything" Commits

**What AI agents often do**:
```bash
git commit -m "Add feature X"
# Changed files: 47 files, +2847 lines, -312 lines
```

**Why it's wrong**:
- Impossible to review
- Can't bisect to find bugs
- Can't cherry-pick individual improvements
- Loses valuable development history

**What you SHOULD do**:
```bash
git commit -m "feat: add IMountStateService interface"
git commit -m "feat: implement MountStateService with JSON persistence"
git commit -m "feat: add mount state tests"
git commit -m "feat: integrate MountStateService in MainViewModel"
git commit -m "docs: update CLAUDE.md with state management pattern"
```

**Red flag**: Commit touching more than 10 files or adding more than 200 lines!

### Quick Mistake Checklist

Before committing ANY code, check:

- [ ] Did I write tests BEFORE implementation?
- [ ] Are all timeout/retry/limit values configurable?
- [ ] Is persistent state in a service, not ViewModel?
- [ ] Am I using IDialogService instead of MessageBox?
- [ ] Is validation in ONE place (orchestrator)?
- [ ] Are all exceptions logged with context?
- [ ] Are all dependencies injected via constructor?
- [ ] Is this a small, focused commit?

If ANY answer is "No", FIX IT before committing!

---

## 🚀 Quick Start Checklist

Before implementing ANY feature:

- [ ] Read CLAUDE.md completely (especially "Lessons Learned" section)
- [ ] Read "Common Mistakes for AI Agents" section above
- [ ] Understand the existing architecture (search for similar code)
- [ ] **DESIGN FIRST**: Plan interfaces, configuration, state management
- [ ] **TESTS FIRST**: Write failing tests (TDD RED)
- [ ] **IMPLEMENT**: Make tests pass (TDD GREEN)
- [ ] **REFACTOR**: Clean up code (TDD REFACTOR)
- [ ] Register in DI (App.xaml.cs)
- [ ] Add XML documentation
- [ ] Commit incrementally (small, focused commits)
- [ ] Update CLAUDE.md if adding new patterns

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

**Version**: 2.0 (Major Update: Added TDD Methodology & Common Mistakes)
**Last Updated**: 2025-11-18
**For**: AI Code Assistants working on LiMount
**Changelog**:
- v2.0: Added TDD methodology, enhanced ultrathink process, added common mistakes section
- v1.0: Initial version with core directives and patterns
