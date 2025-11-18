namespace LiMount.Core.Interfaces;

/// <summary>
/// Interface for Windows drive letter management.
/// </summary>
public interface IDriveLetterService
{
    /// <summary>
    /// Gets all drive letters currently in use on the system.
    /// </summary>
    /// <summary>
/// Retrieves the drive letters currently in use on the system.
/// </summary>
/// <returns>Uppercase drive letters (A–Z) that are currently in use.</returns>
    IReadOnlyList<char> GetUsedLetters();

    /// <summary>
    /// Gets all available (free) drive letters.
    /// Returns letters A-Z that are not in use, sorted Z→A (preferred order).
    /// </summary>
    /// <summary>
/// Retrieves all available drive letters not currently in use, ordered from Z to A.
/// </summary>
/// <returns>An IReadOnlyList of uppercase drive letters that are free, sorted descending from 'Z' to 'A'.</returns>
    IReadOnlyList<char> GetFreeLetters();

    /// <summary>
    /// Checks if a specific drive letter is available (not in use).
    /// </summary>
    /// <param name="letter">Drive letter to check (case-insensitive)</param>
    /// <summary>
/// Checks whether the specified drive letter is available.
/// </summary>
/// <param name="letter">The drive letter to check (A–Z); comparison is case-insensitive.</param>
/// <returns>`true` if the letter is free, `false` otherwise.</returns>
    bool IsLetterAvailable(char letter);
}