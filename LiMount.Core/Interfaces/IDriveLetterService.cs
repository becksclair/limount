namespace LiMount.Core.Interfaces;

/// <summary>
/// Interface for Windows drive letter management.
/// </summary>
public interface IDriveLetterService
{
    /// <summary>
    /// Gets all drive letters currently in use on the system.
    /// </summary>
    /// <returns>A read-only list of uppercase drive letters in use (A–Z).</returns>
    IReadOnlyList<char> GetUsedLetters();

    /// <summary>
    /// Gets all available (free) drive letters.
    /// Returns letters A-Z that are not in use, sorted Z→A (preferred order).
    /// </summary>
    /// <returns>A read-only list of uppercase drive letters that are free, sorted descending (Z→A).</returns>
    IReadOnlyList<char> GetFreeLetters();

    /// <summary>
    /// Checks if a specific drive letter is available (not in use).
    /// </summary>
    /// <param name="letter">Drive letter to check (case-insensitive)</param>
    /// <param name="usedLetters">Optional collection of used drive letters to avoid enumeration. If null, will call GetUsedLetters().</param>
    /// <returns>`true` if the letter is free, `false` otherwise.</returns>
    bool IsLetterAvailable(char letter, IReadOnlyCollection<char>? usedLetters = null);
}