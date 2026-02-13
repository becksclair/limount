using LiMount.Core.Models;
using LiMount.Core.Results;

namespace LiMount.Core.Interfaces;

/// <summary>
/// Creates and removes Windows integration surfaces for mounted UNC paths.
/// </summary>
public interface IWindowsAccessService
{
    /// <summary>
    /// Creates the configured Windows integration for the provided request.
    /// </summary>
    Task<Result<WindowsAccessInfo>> CreateAccessAsync(
        WindowsAccessRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes previously created Windows integration.
    /// </summary>
    Task<Result> RemoveAccessAsync(
        WindowsAccessInfo accessInfo,
        CancellationToken cancellationToken = default);
}

