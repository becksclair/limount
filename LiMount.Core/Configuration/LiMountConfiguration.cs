namespace LiMount.Core.Configuration;

/// <summary>
/// Root configuration for LiMount application.
/// Loaded from appsettings.json and injectable via IOptions&lt;LiMountConfiguration&gt;.
/// </summary>
public class LiMountConfiguration
{
    /// <summary>
    /// Configuration section key in appsettings.json.
    /// </summary>
    public const string SectionName = "LiMount";

    /// <summary>
    /// Configuration for PowerShell script execution.
    /// </summary>
    public ScriptExecutionConfig ScriptExecution { get; set; } = new();

    /// <summary>
    /// Configuration for mount operations.
    /// </summary>
    public MountOperationsConfig MountOperations { get; set; } = new();

    /// <summary>
    /// Configuration for mount history tracking.
    /// </summary>
    public HistoryConfig History { get; set; } = new();

    /// <summary>
    /// Configuration for initialization behavior.
    /// </summary>
    public InitializationConfig Initialization { get; set; } = new();
}

/// <summary>
/// Configuration for PowerShell script execution behavior.
/// </summary>
public class ScriptExecutionConfig
{
    /// <summary>
    /// Maximum time in seconds to wait for elevated script output temp file to appear.
    /// </summary>
    public int TempFilePollingTimeoutSeconds { get; set; } = 5;

    /// <summary>
    /// Interval in milliseconds between checks when polling for temp file.
    /// </summary>
    public int PollingIntervalMs { get; set; } = 100;
}

/// <summary>
/// Configuration for mount operation behavior.
/// </summary>
public class MountOperationsConfig
{
    /// <summary>
    /// Maximum number of retry attempts when verifying UNC path accessibility.
    /// </summary>
    public int UncAccessibilityRetries { get; set; } = 5;

    /// <summary>
    /// Delay in milliseconds between UNC accessibility retry attempts.
    /// </summary>
    public int UncAccessibilityDelayMs { get; set; } = 500;

    /// <summary>
    /// Timeout in milliseconds when checking UNC path accessibility during reconciliation.
    /// </summary>
    public int ReconcileUncAccessibilityTimeoutMs { get; set; } = 2000;
}

/// <summary>
/// Configuration for mount history tracking.
/// </summary>
public class HistoryConfig
{
    /// <summary>
    /// Maximum number of history entries to retain.
    /// </summary>
    public int MaxEntries { get; set; } = 100;

    /// <summary>
    /// Optional explicit path to history file. If null, uses default location in AppData.
    /// </summary>
    public string? FilePath { get; set; }

    /// <summary>
    /// Optional explicit path to mount state file. If null, uses default location in AppData.
    /// </summary>
    public string? StateFilePath { get; set; }
}

/// <summary>
/// Configuration for application initialization behavior.
/// </summary>
public class InitializationConfig
{
    /// <summary>
    /// Maximum number of retry attempts for ViewModel initialization.
    /// </summary>
    public int MaxRetries { get; set; } = 2;

    /// <summary>
    /// Base delay in milliseconds for exponential backoff during initialization retries.
    /// </summary>
    public int BaseDelayMs { get; set; } = 1000;

    /// <summary>
    /// Maximum delay in milliseconds for exponential backoff during initialization retries.
    /// </summary>
    public int MaxDelayMs { get; set; } = 10000;

    /// <summary>
    /// Whether to automatically reconcile mount state on startup.
    /// </summary>
    public bool AutoReconcileMounts { get; set; } = true;
}
