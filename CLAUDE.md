# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

**LiMount** is a Windows desktop application that mounts Linux disk partitions into WSL2 and maps them as Windows drive letters. Built with C# WPF (.NET 8) and PowerShell scripts.

**Key Technologies:**
- .NET 8 (LTS) with `net8.0-windows` target framework
- WPF (Windows Presentation Foundation)
- CommunityToolkit.Mvvm (v8.3.2) for MVVM helpers
- System.Management (v10.0.0) for WMI disk enumeration
- Serilog for file logging
- PowerShell for elevated WSL operations

**Platform:** Windows-only. Services use `[SupportedOSPlatform("windows")]` attributes.

## Building and Running

### Build Commands

```bash
# Restore dependencies
dotnet restore

# Build entire solution
dotnet build --configuration Debug
dotnet build --configuration Release

# Run the WPF application
dotnet run --project LiMount.App

# Build a specific project
dotnet build LiMount.Core
dotnet build LiMount.App
```

### Development Notes

- **Testing**: Comprehensive unit test project exists in `LiMount.Tests/` with 27+ tests
- Application requires Administrator elevation for mounting operations
- The application creates logs at: `%LocalAppData%\LiMount\logs\limount-*.log` (production mode only)
- PowerShell scripts are located in `scripts/` directory and must be accessible at runtime

## Project Structure

```
LiMount.sln              # Solution file with 3 projects
├── LiMount.Core/        # Core library (Windows-only .NET 8)
│   ├── Configuration/   # Configuration models for IOptions<T>
│   ├── Interfaces/      # Service contracts
│   ├── Models/          # Data transfer objects and results
│   └── Services/        # Business logic and WMI/PowerShell interactions
├── LiMount.App/         # WPF application (Windows-only .NET 8)
│   ├── Services/        # App-specific services (DialogService)
│   ├── ViewModels/      # MVVM ViewModels
│   ├── Views/           # Additional windows (HistoryWindow)
│   ├── Converters/      # WPF value converters
│   ├── MainWindow.xaml  # Main UI
│   ├── App.xaml.cs      # DI configuration and startup
│   └── appsettings.json # Application configuration
├── LiMount.Tests/       # Unit test project (.NET 8)
│   └── Services/        # Service tests (xUnit + Moq + FluentAssertions)
└── scripts/             # PowerShell helper scripts
    ├── Mount-LinuxDiskCore.ps1    # Elevated: WSL mount
    ├── Map-WSLShareToDrive.ps1    # Non-elevated: Drive mapping
    ├── Unmount-LinuxDisk.ps1      # Elevated: WSL unmount
    └── Unmap-DriveLetter.ps1      # Non-elevated: Drive unmapping
```

## Architecture

### Layered Design

**LiMount.Core** (Business Logic):
- **Services Layer**: Platform-specific Windows services that interact with WMI and PowerShell
  - `DiskEnumerationService`: Uses WMI (`Win32_DiskDrive`, `Win32_DiskPartition`, `Win32_LogicalDisk`) to enumerate disks and partitions
  - `DriveLetterService`: Manages Windows drive letters using `DriveInfo.GetDrives()`
  - `ScriptExecutor`: Executes PowerShell scripts with proper elevation handling
  - `MountOrchestrator`: Orchestrates mount + mapping workflow with retry logic
  - `UnmountOrchestrator`: Orchestrates unmount + unmapping workflow
  - `KeyValueOutputParser`: Parses `key=value` output from PowerShell scripts

**LiMount.App** (Presentation):
- **MVVM Pattern**: Uses CommunityToolkit.Mvvm source generators
  - `MainViewModel`: Main application logic with `[ObservableProperty]` and `[RelayCommand]` attributes
  - Async initialization pattern to keep UI responsive during disk enumeration
  - Progress reporting via `IProgress<string>` interface

### Dependency Injection

Configured in `App.xaml.cs` using `Microsoft.Extensions.DependencyInjection`:

```csharp
// Core services (Singleton)
services.AddSingleton<IDiskEnumerationService, DiskEnumerationService>();
services.AddSingleton<IDriveLetterService, DriveLetterService>();
services.AddSingleton<IScriptExecutor, ScriptExecutor>();

// Orchestrators (Transient)
services.AddTransient<IMountOrchestrator, MountOrchestrator>();
services.AddTransient<IUnmountOrchestrator, UnmountOrchestrator>();

// ViewModels and Windows (Transient)
services.AddTransient<MainViewModel>();
services.AddTransient<MainWindow>();
```

### PowerShell Script Execution

**Two execution modes:**

1. **Elevated (Mount/Unmount)**: Uses `ProcessStartInfo` with `Verb = "runas"` and `UseShellExecute = true`
   - Cannot redirect stdout/stderr, so scripts write to temp files
   - Temp file pattern: `%TEMP%\limount_mount_{diskIndex}_{partition}.txt` or `limount_unmount_{diskIndex}.txt`
   - ScriptExecutor polls for temp file with timeout

2. **Non-Elevated (Mapping/Unmapping)**: Uses `UseShellExecute = false` with redirected stdout/stderr
   - Direct capture of script output

**Script Output Format**: All scripts use machine-readable `key=value` format:
```
STATUS=OK
DistroName=Ubuntu
MountPathLinux=/mnt/wsl/PHYSICALDRIVE2p1
MountPathUNC=\\wsl$\Ubuntu\mnt\wsl\PHYSICALDRIVE2p1
```

### Key Components

**DiskEnumerationService**:
- Queries WMI for disk and partition information
- Implements heuristics to identify Linux partitions (no drive letter, non-NTFS/FAT32)
- Filters out system/boot disks for safety
- Returns `DiskInfo` objects with nested `PartitionInfo` collections

**ScriptExecutor**:
- Auto-locates `scripts/` directory using relative path search from application base directory
- Handles both elevated and non-elevated PowerShell execution
- Parses script output using `KeyValueOutputParser`
- Temp file cleanup after elevated operations

**MountOrchestrator**:
- Two-phase operation: (1) WSL mount, (2) drive mapping
- Includes UNC path accessibility verification with retry (5 attempts, 500ms delay)
- Progress reporting throughout the workflow
- Returns `MountAndMapResult` with detailed success/failure information

**MainViewModel**:
- Async initialization pattern: `InitializeAsync()` called from `MainWindow.OnLoaded`
- Disk enumeration runs on background thread, UI updates on dispatcher
- Auto-selection of first available disk, partition, and drive letter
- Command execution guarded by `CanMount()` and `CanOpenExplorer()` methods

## Coding Patterns

### MVVM with Source Generators

Use CommunityToolkit.Mvvm source generators for properties and commands:

```csharp
[ObservableProperty]
private bool _isBusy;  // Generates IsBusy property with INotifyPropertyChanged

[RelayCommand(CanExecute = nameof(CanMount))]
private async Task MountAsync() { }  // Generates MountCommand
```

### Result Objects

All operations return strongly-typed result objects with `Success` flag:
- `MountResult`: WSL mount operation results
- `MappingResult`: Drive mapping results
- `MountAndMapResult`: Combined operation results
- `UnmountResult`: WSL unmount results
- `UnmappingResult`: Drive unmapping results

Each result class has `FromDictionary()` static method for parsing script output.

### Async Patterns

- Services use `async/await` throughout
- UI operations check thread using `Application.Current.Dispatcher.InvokeAsync()`
- `IProgress<string>` for progress reporting from orchestrators to ViewModel
- `CancellationToken` support in initialization

## Common Tasks

### Adding a New Service

1. Define interface in `LiMount.Core/Interfaces/I{ServiceName}.cs`
2. Implement service in `LiMount.Core/Services/{ServiceName}.cs`
3. Add `[SupportedOSPlatform("windows")]` attribute if using Windows-specific APIs
4. Register in `App.xaml.cs` DI configuration
5. Inject into ViewModels or orchestrators via constructor

### Adding a New PowerShell Script

1. Create script in `scripts/` directory with proper parameter validation
2. Implement machine-readable output using `key=value` format
3. Add execution method in `ScriptExecutor` (elevated or non-elevated)
4. Create corresponding result model in `LiMount.Core/Models/`
5. Add `FromDictionary()` method to parse script output

### Modifying the UI

1. Update XAML in `MainWindow.xaml`
2. Add properties/commands to `MainViewModel.cs` using `[ObservableProperty]` and `[RelayCommand]`
3. Create value converters in `LiMount.App/Converters/` if needed for binding transformations
4. Wire up bindings in XAML

## Important Constraints

### Safety Mechanisms

- **System/Boot Disk Protection**: `DiskEnumerationService.GetCandidateDisks()` filters out disks marked as system or boot disks
- **Elevated Operations**: Mount/unmount operations require Administrator privileges (UAC prompt)
- **Filesystem Validation**: Mount script validates filesystem type against allowed list (ext4, xfs, btrfs, vfat)

### Known Limitations

- No automated unmount on application exit
- Drive mappings are session-based (not persistent across reboots)
- Single WSL distro auto-detection (first available distro)
- Integration tests require Windows environment (can't run on Linux CI)

### Script Path Resolution

`ScriptExecutor` searches for `scripts/` directory in multiple locations:
1. `{AppDirectory}/scripts`
2. `{AppDirectory}/../../../scripts` (for development builds)
3. `{AppDirectory}/../../../../scripts`
4. `{AppDirectory}/../../../../../scripts`

When adding or modifying scripts, ensure they remain in the root `scripts/` directory.

## Logging

Application uses Serilog with conditional file logging:
- **Debug Mode** (debugger attached): Logs to Debug output only
- **Production Mode**: Logs to `%LocalAppData%\LiMount\logs\limount-{Date}.log`
  - Rolling daily logs, retains last 7 days
  - 10MB file size limit with rollover
  - Configured in `App.xaml.cs` startup

To enable production logging in debug mode, set environment variable: `DOTNET_ENVIRONMENT=Production`

## WSL Integration

### Mount Operation Flow

1. `wsl --mount \\.\PHYSICALDRIVE{Index} --partition {Partition} --type {FsType}`
2. Detect mount path by querying `/mnt/wsl/` directory in WSL
3. Build UNC path: `\\wsl$\{DistroName}{MountPathLinux}`
4. Verify UNC accessibility with retry logic
5. Map UNC to drive letter using `net use` or similar

### Unmount Operation Flow

1. Remove drive letter mapping using Windows API
2. `wsl --unmount \\.\PHYSICALDRIVE{Index}`

### Requirements

- Windows 11 Build 22000+ or Microsoft Store WSL version
- WSL2 must be running (at least one distro)
- Administrator privileges for mount/unmount operations

## Design Principles & Best Practices

### Core Architectural Principles

1. **Dependency Injection First**: All services must be injected, never use `new` for services
2. **Interface-Based Design**: Every service must have an interface for testability
3. **Single Responsibility**: Each service should have one clear purpose
4. **Result Objects Over Exceptions**: Use typed result objects for expected failures
5. **Async All the Way**: All I/O operations must be async to keep UI responsive

### State Management

**IMPORTANT**: Mount state must be managed centrally, not in ViewModels.

- **Use**: `IMountStateService` for tracking active mounts
- **Persist**: State must survive application restarts
- **Reconcile**: Always reconcile on startup to detect orphaned mounts
- **Never**: Store mount state only in ViewModel properties

**Example**:
```csharp
// WRONG: State lost on app close
private int? _currentMountedDiskIndex;

// RIGHT: State persisted and queryable
var activeMount = await _mountStateService.GetMountForDiskAsync(diskIndex);
```

### Configuration Management

**All tunable values must be in appsettings.json**:

```csharp
// WRONG: Hardcoded constant
private const int Timeout = 5;

// RIGHT: Configured value
private readonly LiMountConfiguration _config;
var timeout = _config.ScriptExecution.TempFilePollingTimeoutSeconds;
```

### Validation Strategy

**Validate once, centrally**:

- All validation logic belongs in orchestrators (before calling executors)
- Return structured error results, don't throw exceptions for validation failures
- Include parameter name in error message for debugging

```csharp
// In orchestrator
if (diskIndex < 0)
{
    return MountAndMapResult.CreateFailure(
        diskIndex, partition, 
        "Disk index must be non-negative", 
        "validation");
}
```

### Testing Philosophy

**Write tests BEFORE or WITH implementation**:

1. **Unit Tests**: Mock all dependencies, test business logic
2. **Integration Tests**: Test PowerShell scripts on real Windows
3. **UI Tests**: Use `IDialogService` abstraction for testability

**Example Test Structure**:
```csharp
[Fact]
public async Task MountOrchestrator_InvalidDiskIndex_ReturnsValidationError()
{
    // Arrange
    var mockExecutor = new Mock<IScriptExecutor>();
    var orchestrator = new MountOrchestrator(mockExecutor.Object);
    
    // Act
    var result = await orchestrator.MountAndMapAsync(-1, 1, 'Z');
    
    // Assert
    Assert.False(result.Success);
    Assert.Equal("validation", result.FailedStep);
}
```

## Development Process & Best Practices

### Recommended Development Order

When building a new feature, follow this sequence to avoid common pitfalls:

**Phase 1: Design & Planning (Before Any Code)**
1. **Define Requirements** - What problem are we solving?
2. **Design Interfaces** - What contracts are needed?
3. **Plan Configuration** - What should be tunable?
4. **Consider State** - Does this need persistence?
5. **Think About Testing** - How will we verify it works?

**Phase 2: Infrastructure First (Foundation)**
1. **Create Configuration Schema** - Add to `LiMountConfiguration.cs` and `appsettings.json`
2. **Define Interfaces** - Create in `LiMount.Core/Interfaces/`
3. **Create Models** - Add result objects in `LiMount.Core/Models/`
4. **Write Tests** - Start with test structure even if not runnable yet

**Phase 3: Implementation (Build)**
1. **Implement Services** - Follow existing patterns
2. **Register in DI** - Update `App.xaml.cs`
3. **Integrate into ViewModels** - Wire up commands
4. **Update UI** - Add XAML bindings

**Phase 4: Verification (Test)**
1. **Unit Tests** - Test business logic with mocks
2. **Manual Testing** - Run on Windows, verify behavior
3. **Document** - Update CLAUDE.md if adding patterns

**Example: Adding Environment Validation**

✅ **Correct Order:**
```
1. Design: Need to check WSL, distros, Windows version
2. Config: No config needed (always validate)
3. Interface: IEnvironmentValidationService
4. Model: EnvironmentValidationResult with errors/suggestions
5. Tests: Write test cases (even if can't run on Linux)
6. Implement: EnvironmentValidationService
7. Register: Add to DI
8. Integrate: Call during InitializeAsync
9. UI: Show error dialog with actionable messages
```

❌ **Wrong Order:**
```
1. Start writing service code immediately
2. Realize need to add configuration mid-way
3. Hardcode values "temporarily"
4. Forget to add tests
5. Ship with hardcoded values still in place
```

### Test-Driven Development (TDD)

**Philosophy**: Write tests BEFORE or WITH implementation, not after.

**Why TDD Matters:**
- Tests drive better API design
- Catches bugs during development, not in production
- Documents expected behavior
- Enables confident refactoring
- Forces thinking about edge cases

**TDD Cycle for LiMount:**

```
1. RED: Write a failing test
   [Fact]
   public async Task MountOrchestrator_NegativeDiskIndex_ReturnsValidationError()
   {
       var result = await orchestrator.MountAndMapAsync(-1, 1, 'Z');
       result.Success.Should().BeFalse();
       result.FailedStep.Should().Be("validation");
   }

2. GREEN: Write minimal code to pass
   if (diskIndex < 0)
   {
       return MountAndMapResult.CreateFailure(diskIndex, partition,
           "Disk index must be non-negative", "validation");
   }

3. REFACTOR: Improve code quality
   - Extract validation method
   - Add logging
   - Improve error message
```

**Testing Strategy:**

| Test Type | Purpose | When | Tools |
|-----------|---------|------|-------|
| **Unit Tests** | Test business logic in isolation | During development | xUnit, Moq, FluentAssertions |
| **Integration Tests** | Test PowerShell scripts on real Windows | Before release | xUnit, real environment |
| **Manual Tests** | Verify UI behavior | Before release | Windows machine |

**Coverage Goals:**
- Unit Test Coverage: 80%+ of business logic
- Integration Test Coverage: All PowerShell scripts
- Critical Path Coverage: 100% of mount/unmount workflows

### Configuration-First Approach

**Rule**: All tunable values go in `appsettings.json` from day one.

**Before Writing Any Service:**

1. **Identify Tunable Values**
   - Timeouts
   - Retry counts
   - Delays
   - File paths
   - Limits

2. **Add to Configuration**
   ```csharp
   // LiMount.Core/Configuration/LiMountConfiguration.cs
   public class MyFeatureConfig
   {
       public int MaxRetries { get; set; } = 3;
       public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
   }
   ```

3. **Update appsettings.json**
   ```json
   {
     "LiMount": {
       "MyFeature": {
         "MaxRetries": 3,
         "Timeout": "00:00:30"
       }
     }
   }
   ```

4. **Then Inject into Service**
   ```csharp
   public MyService(IOptions<LiMountConfiguration> config)
   {
       _maxRetries = config.Value.MyFeature.MaxRetries;
   }
   ```

**Benefits:**
- No recompilation needed to tune
- Different configs for dev/test/prod
- Testable (inject test config)
- User can customize

### State Management First

**Rule**: Design state persistence BEFORE building features that need it.

**Questions to Ask:**
- Does this state need to survive app restart? → Use `IMountStateService`
- Is this historical data? → Use `IMountHistoryService`
- Is this transient UI state? → ViewModel property is OK

**State Design Checklist:**
- [ ] Identified what needs persistence
- [ ] Designed state model (e.g., `ActiveMount`)
- [ ] Chose storage mechanism (JSON file)
- [ ] Defined service interface (`IMountStateService`)
- [ ] Implemented persistence layer
- [ ] Registered in DI
- [ ] Added reconciliation logic
- [ ] **Then** built features that use it

**Anti-Pattern:**
```csharp
// ❌ State in ViewModel only - lost on restart!
public class MainViewModel
{
    private int? _currentMountedDiskIndex;
}
```

**Correct Pattern:**
```csharp
// ✅ State in service - persists across restarts
public class MainViewModel
{
    private readonly IMountStateService _stateService;

    public async Task<bool> IsMountedAsync(int diskIndex)
    {
        return await _stateService.IsDiskMountedAsync(diskIndex);
    }
}
```

### Incremental Commits

**Rule**: Commit small, working changes frequently.

**Commit Granularity:**

✅ **Good:**
```bash
git commit -m "feat: add IEnvironmentValidationService interface"
git commit -m "feat: add EnvironmentValidationResult model"
git commit -m "feat: implement EnvironmentValidationService"
git commit -m "feat: register environment validation in DI"
git commit -m "feat: integrate environment validation into startup"
git commit -m "test: add environment validation tests"
```

❌ **Bad:**
```bash
git commit -m "add environment validation feature"  # One massive commit
```

**Why This Matters:**
- Easy to review
- Easy to revert specific changes
- Clear history for debugging
- Documents thought process
- Enables bisecting bugs

### Code Review Before Merge

**Self-Review Checklist:**

**Architecture Compliance:**
- [ ] All services have interfaces
- [ ] All dependencies injected via constructor
- [ ] No hardcoded configuration values
- [ ] State management uses appropriate service
- [ ] Validation in orchestrators, not scattered

**Code Quality:**
- [ ] XML documentation on public members
- [ ] Error handling with logging
- [ ] Async/await for all I/O
- [ ] Result objects for operations
- [ ] No silent exception swallowing

**Testing:**
- [ ] Unit tests written/updated
- [ ] Edge cases covered
- [ ] Error cases tested
- [ ] Integration test needs documented

**Documentation:**
- [ ] CLAUDE.md updated if adding patterns
- [ ] README updated if user-facing
- [ ] Commit messages descriptive
- [ ] Code comments for complex logic

## Lessons Learned & Common Pitfalls

### Critical Mistakes We Made (And How We Fixed Them)

These are real mistakes made during development of LiMount, documented so you don't repeat them:

#### 1. **❌ Test-Last Instead of Test-First**

**What Happened:**
- Built all features first, added tests at the end
- Had to retrofit tests onto existing code
- Tests felt like an afterthought

**The Problem:**
- Missed bugs that TDD would have caught
- APIs designed for implementation, not testability
- Tests document behavior AFTER it was written

**How We Fixed It:**
- Created comprehensive test project with 27+ tests
- Added xUnit, Moq, FluentAssertions
- Documented TDD process above

**Lesson Learned:** Write tests BEFORE or WITH implementation. Tests should drive API design, not document it afterwards.

#### 2. **❌ State Management as an Afterthought**

**What Happened:**
- Started with mount state in ViewModel only
- Users lost all state when app restarted
- Had to refactor to add `MountStateService` later

**The Problem:**
- Poor UX (users couldn't see what was mounted)
- Breaking change to existing ViewModels
- Could have been designed right from the start

**How We Fixed It:**
- Created `IMountStateService` with JSON persistence
- Created `ActiveMount` model for state tracking
- Integrated into ViewModels properly

**Lesson Learned:** Design state persistence BEFORE building features. Ask "does this need to survive restart?" immediately.

#### 3. **❌ Environment Validation Added Last**

**What Happened:**
- Built disk enumeration, mounting, unmounting first
- Added environment validation at the very end
- Users could waste time if WSL wasn't installed

**The Problem:**
- User frustration: "Why doesn't it work?" → "Oh, WSL isn't installed"
- Debugging time wasted on environment issues

**How We Fixed It:**
- Created `IEnvironmentValidationService`
- Added comprehensive checks (WSL, distros, Windows version)
- Integrated into startup with actionable error messages

**Lesson Learned:** Environment validation should be the FIRST feature, not the last. Validate upfront, fail fast.

#### 4. **❌ Configuration Hardcoded Then Extracted**

**What Happened:**
- Started with constants in services (`const int retries = 5`)
- Had to refactor to extract to appsettings.json
- Updated multiple service constructors

**The Problem:**
- Can't tune without recompiling
- Breaking change to add configuration
- "Temporary" hardcoded values become permanent

**How We Fixed It:**
- Created `LiMountConfiguration` hierarchy
- Added `appsettings.json` with all tunable values
- Updated all services to use `IOptions<T>`

**Lesson Learned:** All tunable values go in configuration FROM DAY ONE. No exceptions.

#### 5. **❌ Storing UI State in ViewModel Without Persistence**
   - Problem: Mount state lost when app closes
   - Solution: Use `IMountStateService` for all mount tracking
   - Impact: Users can't see what's mounted after restart
   - **Status**: ✅ Fixed with persistent state service

#### 6. **❌ Direct MessageBox Calls in ViewModel**
   - Problem: ViewModel becomes untestable
   - Solution: Use `IDialogService` abstraction
   - Impact: Can't write unit tests for confirmation flows
   - **Status**: ✅ Fixed with `IDialogService`

#### 7. **❌ Duplicate Validation Logic**
   - Problem: Validation inconsistencies, maintenance burden
   - Solution: Validate once in orchestrators
   - Impact: Bugs from inconsistent validation
   - **Status**: ✅ Fixed, all validation in orchestrators

#### 8. **❌ Silent Exception Swallowing**
   - Problem: Errors disappear, impossible to debug
   - Solution: Always log exceptions, even if handled gracefully
   - Impact: Production debugging nightmares
   - **Status**: ✅ Fixed with comprehensive logging

### What We Got Right

These patterns worked exceptionally well and should be continued:

✅ **Interface-Based Design** - Made testing trivial, enabled mocking
✅ **Dependency Injection Throughout** - Clean, testable, maintainable
✅ **Result Objects Over Exceptions** - Explicit error handling, easier testing
✅ **Orchestrator Pattern** - Clean separation of workflow from execution
✅ **Async/Await Everywhere** - UI stayed responsive
✅ **Dialog Service Abstraction** - ViewModels fully testable
✅ **MVVM with Source Generators** - Minimal boilerplate
✅ **Structured Logging** - Serilog with file output for diagnostics
✅ **Incremental Git Commits** - Clear history for debugging

### What Worked Well

✅ **Orchestrator Pattern**: Clean separation of workflow from execution  
✅ **Result Objects**: Better than exceptions for expected failures  
✅ **Async/Await**: UI stays responsive during long operations  
✅ **DI with Interfaces**: Makes everything testable  
✅ **MVVM with Source Generators**: Minimal boilerplate  
✅ **Structured Logging**: Serilog with file output for diagnostics  

## Architectural Decisions & Rationale

### Why Orchestrators Instead of Fat Services?

**Decision**: Use separate orchestrator classes for complex workflows

**Rationale**:
- `ScriptExecutor` should only execute scripts, not orchestrate multi-step flows
- Orchestrators provide progress reporting and retry logic
- Easy to test workflows by mocking `IScriptExecutor`
- Clear separation: Execution vs. Coordination

### Why Result Objects Instead of Exceptions?

**Decision**: Return typed result objects for all operations

**Rationale**:
- Mount failures are expected, not exceptional
- Results can carry rich context (which step failed, error details)
- Easier to test (no need to catch exceptions)
- Forces caller to check success before using data

### Why Singleton Services?

**Decision**: `DiskEnumerationService`, `DriveLetterService`, `ScriptExecutor` are singletons

**Rationale**:
- No mutable state, safe to reuse
- WMI queries are expensive, service can cache if needed
- Reduces memory allocation pressure
- Script path resolution only needs to happen once

### Why Transient Orchestrators?

**Decision**: `MountOrchestrator`, `UnmountOrchestrator` are transient

**Rationale**:
- Each operation is independent
- No state to carry between operations
- Cleaner lifecycle management
- Easier to reason about concurrency

## Future Improvements & Roadmap

### High Priority (Should Implement Soon)

1. **Environment Validation on Startup**
   - Check WSL availability
   - Verify Windows version compatibility
   - Detect at least one WSL distro installed
   - Show actionable error if environment invalid

2. **Structured Progress Reporting**
   - Replace `IProgress<string>` with `IProgress<MountProgressInfo>`
   - Include step number, percent complete, estimated time
   - Enable progress bar UI instead of just text

3. **Automatic Retry for Transient Failures**
   - Implement `IRetryPolicy` service
   - Auto-retry UNC accessibility checks
   - Configurable retry behavior per operation type

### Medium Priority (Nice to Have)

4. **Mount History UI**
   - Display history in DataGrid
   - Filter by success/failure
   - Export history to CSV

5. **Multiple Concurrent Mounts**
   - Support mounting multiple disks simultaneously
   - Track all active mounts
   - Bulk unmount operation

6. **Mount Verification**
   - Periodic health checks for active mounts
   - Detect and notify if mount becomes inaccessible
   - Auto-cleanup stale mounts

### Low Priority (Future Features)

7. **Persistent Drive Mappings**
   - Option to make mappings survive reboot
   - Registry-based persistence
   - Startup mount automation

8. **Multi-Distro Support**
   - Choose which WSL distro to mount into
   - Mount same disk into multiple distros
   - Distro-specific settings

## Key Services Reference

### Core Services (Must Use)

| Service | Purpose | Lifetime | When to Use |
|---------|---------|----------|-------------|
| `IMountStateService` | Track active mounts | Singleton | Always query before mount/unmount |
| `IMountHistoryService` | Log operation history | Singleton | Auto-logged by orchestrators |
| `IDiskEnumerationService` | Find mountable disks | Singleton | On refresh, on startup |
| `IDriveLetterService` | Find free drive letters | Singleton | Before mount, on refresh |
| `IScriptExecutor` | Execute PowerShell | Singleton | Only via orchestrators |
| `IMountOrchestrator` | Mount workflow | Transient | For mount operations |
| `IUnmountOrchestrator` | Unmount workflow | Transient | For unmount operations |
| `IDialogService` | Show user dialogs | Singleton | For all confirmations/alerts |

### Configuration Access

```csharp
// Inject configuration
public class MyService
{
    private readonly LiMountConfiguration _config;
    
    public MyService(IOptions<LiMountConfiguration> config)
    {
        _config = config.Value;
    }
    
    public async Task DoSomething()
    {
        var timeout = _config.ScriptExecution.TempFilePollingTimeoutSeconds;
        // Use configured value
    }
}
```

## Troubleshooting Guide

### "No disks found"
- Check WMI is working: `Get-WmiObject Win32_DiskDrive`
- Verify user has permissions
- Check logs: `%LocalAppData%\LiMount\logs\`

### "Mount fails with timeout"
- Increase `ScriptExecution.TempFilePollingTimeoutSeconds` in appsettings.json
- Check if UAC dialog was dismissed
- Verify script execution policy allows running PowerShell

### "Drive mapping fails"
- Check if drive letter already in use
- Verify UNC path exists: `Test-Path \\wsl$\Ubuntu\...`
- Increase `MountOperations.UncAccessibilityRetries`

### "State lost after restart"
- Verify `IMountStateService` is registered as singleton
- Check state file exists: `%LocalAppData%\LiMount\mount-state.json`
- Enable debug logging to see state operations

## Contributing Guidelines

### Before Starting Work

1. Read this entire CLAUDE.md file
2. Read AGENTS.md for AI-specific guidance
3. Review existing similar implementations
4. Check TODO.md for planned work
5. Ensure change aligns with architecture principles

### Code Review Checklist

- [ ] All services injected via constructor
- [ ] All services have interfaces
- [ ] Configuration values not hardcoded
- [ ] Error handling with logging
- [ ] Parameter validation at orchestrator level
- [ ] Unit tests written/updated
- [ ] CLAUDE.md updated if adding patterns
- [ ] XML documentation on public members

### Git Workflow

1. Create feature branch: `feature/short-description`
2. Make incremental commits (not one massive commit)
3. Write descriptive commit messages
4. Test on Windows before pushing
5. Create PR with clear description

---

**Last Updated**: 2025-11-18  
**Version**: 2.0 (After architectural improvements)  
**Status**: Production-ready with modern best practices
