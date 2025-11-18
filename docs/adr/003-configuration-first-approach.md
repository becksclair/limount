# ADR-003: Configuration-First Approach for All Tunable Values

**Status**: Accepted

**Date**: 2025-11-18

**Deciders**: Development Team

**Context**: Timeout values, retry counts, and other tunable parameters were initially hardcoded, making it impossible to adjust behavior without recompiling.

---

## Context and Problem Statement

The application has many tunable parameters that affect behavior:
- Script execution timeout (how long to wait for PowerShell)
- UNC accessibility retry count (how many times to check if path is accessible)
- UNC accessibility retry delay (how long between retries)
- Temp file polling timeout (elevated scripts write to temp files)
- History retention days (how long to keep mount history)
- Log file retention days (how long to keep log files)

Initially, these were hardcoded constants:
```csharp
private const int TIMEOUT_SECONDS = 10;
private const int MAX_RETRIES = 5;
private const int RETRY_DELAY_MS = 500;
```

This caused problems:
- Can't tune for slow systems without recompiling
- Can't adjust for testing (need faster timeouts)
- Can't let users customize behavior
- Developers forget which values should be tunable

We need a consistent approach to configuration that makes tunable values explicit and adjustable.

## Decision Drivers

- **Flexibility**: Users on slow systems need higher timeouts
- **Testing**: Tests need shorter timeouts (fast feedback)
- **Maintainability**: Don't want magic numbers scattered in code
- **Discoverability**: Should be obvious what can be configured
- **Type Safety**: Configuration should be strongly-typed, not strings

## Considered Options

### Option 1: Hardcoded Constants

```csharp
public class MountOrchestrator
{
    private const int UNC_RETRY_COUNT = 5;
    private const int UNC_RETRY_DELAY_MS = 500;
    private const int TIMEOUT_SECONDS = 10;

    // Use constants directly
}
```

**Pros**:
- Simple
- Fast (compile-time constants)
- No configuration system needed

**Cons**:
- Can't adjust without recompiling
- Can't customize per-environment
- Magic numbers scattered throughout code
- Testing requires changing constants

### Option 2: Environment Variables

```csharp
var timeout = int.Parse(Environment.GetEnvironmentVariable("LIMOUNT_TIMEOUT") ?? "10");
```

**Pros**:
- No configuration files needed
- Per-environment customization

**Cons**:
- Not discoverable (how do users know what env vars exist?)
- Type-unsafe (everything is a string)
- Error-prone (parsing can fail)
- No validation
- Not user-friendly

### Option 3: Configuration File with IOptions Pattern

```csharp
// appsettings.json
{
  "LiMount": {
    "ScriptExecution": {
      "TempFilePollingTimeoutSeconds": 10,
      "TempFilePollingIntervalMs": 100
    },
    "MountOperations": {
      "UncAccessibilityRetries": 5,
      "UncAccessibilityRetryDelayMs": 500
    }
  }
}

// Configuration model
public class LiMountConfiguration
{
    public ScriptExecutionConfig ScriptExecution { get; set; } = new();
    public MountOperationsConfig MountOperations { get; set; } = new();
}

public class ScriptExecutionConfig
{
    public int TempFilePollingTimeoutSeconds { get; set; } = 10;
    public int TempFilePollingIntervalMs { get; set; } = 100;
}

// Service uses IOptions
public class MountOrchestrator
{
    private readonly LiMountConfiguration _config;

    public MountOrchestrator(IOptions<LiMountConfiguration> config)
    {
        _config = config.Value;
    }

    public async Task MountAsync()
    {
        var timeout = _config.MountOperations.UncAccessibilityRetries;
        // Use configured value
    }
}
```

**Pros**:
- Strongly-typed configuration
- Easy to discover (all in appsettings.json)
- Can customize per-environment (appsettings.Development.json)
- Validation support
- Default values in code
- User-friendly JSON format

**Cons**:
- More setup (config classes, IOptions registration)
- Requires reading appsettings.json

## Decision Outcome

**Chosen option**: Option 3: Configuration File with IOptions Pattern

**Rationale**:
Hardcoded constants made the application inflexible. Fast systems could use shorter timeouts, slow systems needed longer ones, but nobody could adjust without recompiling. Environment variables are error-prone and not discoverable.

Configuration files with strong typing provide:
- Type safety (no parsing errors)
- Discoverability (users can see all options in appsettings.json)
- Default values (in code, fallback if config is missing)
- Per-environment customization (Development vs. Production)
- User-friendliness (edit JSON file, not recompile)

This is standard .NET practice (ASP.NET Core uses this extensively).

### Consequences

**Positive**:
- Users can tune timeouts for their system
- Tests can use fast timeouts (fast feedback)
- All tunable values are discoverable (in appsettings.json)
- Type-safe (compiler catches errors)
- Easy to add new config values (add to model + JSON)

**Negative**:
- More boilerplate (config classes for each feature)
- Need to inject IOptions into services
- Configuration errors are runtime, not compile-time

**Mitigation**:
- Provide sensible defaults in code
- Add validation on startup (TODO: environment validation)
- Document all config options in CLAUDE.md

**Risks**:
- Risk: User edits appsettings.json with invalid values
- Mitigation: Provide defaults, validate on startup, log warnings

## Implementation Notes

### Configuration Structure

All configuration is under `LiMount` root key in appsettings.json:

```json
{
  "LiMount": {
    "ScriptExecution": {
      "TempFilePollingTimeoutSeconds": 10,
      "TempFilePollingIntervalMs": 100
    },
    "MountOperations": {
      "UncAccessibilityRetries": 5,
      "UncAccessibilityRetryDelayMs": 500
    },
    "History": {
      "FilePath": "%LocalAppData%\\LiMount\\mount-history.json",
      "MaxEntries": 1000,
      "StateFilePath": "%LocalAppData%\\LiMount\\mount-state.json"
    },
    "Logging": {
      "RetentionDays": 7,
      "FileSizeLimitBytes": 10485760
    }
  }
}
```

### Configuration Classes

Defined in `LiMount.Core/Configuration/`:

```csharp
public class LiMountConfiguration
{
    public ScriptExecutionConfig ScriptExecution { get; set; } = new();
    public MountOperationsConfig MountOperations { get; set; } = new();
    public HistoryConfig History { get; set; } = new();
    public LoggingConfig Logging { get; set; } = new();
}

public class ScriptExecutionConfig
{
    public int TempFilePollingTimeoutSeconds { get; set; } = 10;
    public int TempFilePollingIntervalMs { get; set; } = 100;
}

// ... other config classes
```

### Registration in DI

In `App.xaml.cs`:

```csharp
// Load configuration from appsettings.json
var configuration = new ConfigurationBuilder()
    .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .Build();

// Register IOptions
services.Configure<LiMountConfiguration>(configuration.GetSection("LiMount"));
```

### Usage in Services

```csharp
public class MountOrchestrator : IMountOrchestrator
{
    private readonly MountOperationsConfig _config;

    public MountOrchestrator(IOptions<LiMountConfiguration> config, ...)
    {
        _config = config.Value.MountOperations;
    }

    private async Task<string?> WaitForUncAccessibilityAsync(string uncPath)
    {
        // Use configured values
        for (int i = 0; i < _config.UncAccessibilityRetries; i++)
        {
            if (Directory.Exists(uncPath))
                return uncPath;

            await Task.Delay(_config.UncAccessibilityRetryDelayMs);
        }
        return null;
    }
}
```

Implemented in:
- `LiMount.Core/Configuration/LiMountConfiguration.cs`
- `LiMount.App/appsettings.json`
- `LiMount.App/App.xaml.cs` (IOptions registration)

## Validation

Success criteria:
- No hardcoded timeout/retry values in services
- All tunable values in appsettings.json
- Services use IOptions pattern
- Configuration has sensible defaults

Metrics:
- Hardcoded constants removed: 15+ magic numbers
- Configuration values: 12+ tunable parameters
- Services using IOptions: All services

## Links

- Microsoft Docs: [Options pattern in ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/configuration/options)
- CLAUDE.md: "Configuration Management" section
- AGENTS.md: "Should This Be Configurable?" decision framework

---

## Metadata

**Related ADRs**: None (foundational decision)

**Tags**: configuration, architecture, flexibility

**Review Date**: 2026-05-01
