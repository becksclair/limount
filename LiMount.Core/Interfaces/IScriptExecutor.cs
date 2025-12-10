namespace LiMount.Core.Interfaces;

/// <summary>
/// Combined interface for executing PowerShell scripts and commands.
/// Abstracts the execution details to enable testing and different execution strategies.
/// </summary>
/// <remarks>
/// This interface is maintained for backward compatibility.
/// For new code, prefer using the focused interfaces:
/// <see cref="IMountScriptService"/> for mount/unmount operations,
/// <see cref="IDriveMappingService"/> for drive letter mapping,
/// <see cref="IFilesystemDetectionService"/> for filesystem detection.
/// </remarks>
// TODO: Remove IScriptExecutor interface once all external consumers have migrated.
// Internal code (MountOrchestrator, UnmountOrchestrator, BaseMainViewModel, tests) now uses
// focused interfaces. Only DI registration in App.xaml.cs still registers this for
// backward compatibility with any external code that might depend on it.
[Obsolete("Use IMountScriptService, IDriveMappingService, or IFilesystemDetectionService for new code. This interface is maintained for backward compatibility.")]
public interface IScriptExecutor : IMountScriptService, IDriveMappingService, IFilesystemDetectionService
{
    // All methods are now inherited from the focused interfaces.
    // This interface serves as a combined facade for backward compatibility.
}
