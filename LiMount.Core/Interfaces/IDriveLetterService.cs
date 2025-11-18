namespace LiMount.Core.Interfaces;

/// <summary>
/// Interface for Windows drive letter management.
/// </summary>
public interface IDriveLetterService
{
    /// <summary>
    /// Gets all drive letters currently in use on the system.
    /// </summary>
    /// <returns>List of uppercase drive letters (A-Z) that are in use</returns>
    IReadOnlyList<char> GetUsedLetters();

    /// <summary>
    /// Gets all available (free) drive letters.
    /// Returns letters A-Z that are not in use, sorted Z→A (preferred order).
    /// </summary>
    /// <returns>List of free drive letters, sorted Z→A</returns>
    IReadOnlyList<char> GetFreeLetters();

    /// <summary>
    /// Checks if a specific drive letter is available (not in use).
    /// </summary>
    /// <param name="letter">Drive letter to check (case-insensitive)</param>
    /// <returns>True if the letter is free, false if in use</returns>
    bool IsLetterAvailable(char letter);
}
