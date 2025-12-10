using LiMount.Core.Models;

namespace LiMount.Core.Interfaces;

/// <summary>
/// Service interface for drive letter mapping operations.
/// Handles mapping and unmapping UNC paths to Windows drive letters.
/// </summary>
public interface IDriveMappingService
{
    /// <summary>
    /// Executes the Map-WSLShareToDrive.ps1 script to map the specified UNC network path to a drive letter.
    /// </summary>
    /// <param name="driveLetter">The drive letter to assign (e.g., 'Z').</param>
    /// <param name="targetUNC">The UNC path to map (e.g., \\server\share).</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A MappingResult describing the outcome of the mapping operation.</returns>
    Task<MappingResult> ExecuteMappingScriptAsync(
        char driveLetter,
        string targetUNC,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Unmaps the specified drive letter by executing the Unmap-DriveLetter.ps1 script.
    /// </summary>
    /// <param name="driveLetter">Drive letter to unmap (e.g., 'Z').</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The result of the unmapping operation.</returns>
    Task<UnmappingResult> ExecuteUnmappingScriptAsync(char driveLetter, CancellationToken cancellationToken = default);
}
