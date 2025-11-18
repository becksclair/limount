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
/// Gets all drive letters currently assigned on the system.
/// </summary>
/// <returns>A read-only list of uppercase drive letters in use (A–Z).</returns>
    IReadOnlyList<char> GetUsedLetters();

    /// <summary>
    /// Gets all available (free) drive letters.
    /// Returns letters A-Z that are not in use, sorted Z→A (preferred order).
    /// </summary>
    /// <summary>
/// Gets all available drive letters not currently in use, ordered from Z to A.
/// </summary>
/// <returns>A read-only list of uppercase drive letters that are free, sorted descending (Z→A).</returns>
    IReadOnlyList<char> GetFreeLetters();

    /// <summary>
    /// Checks if a specific drive letter is available (not in use).
    /// </summary>
    /// <param name="letter">Drive letter to check (case-insensitive)</param>
    /// <summary>
/// Determines whether a specific drive letter is available (not currently assigned).
/// </summary>
/// <param name="letter">The drive letter to check; comparison is case-insensitive (A–Z).</param>
/// <returns>`true` if the letter is free, `false` otherwise.</returns>
    bool IsLetterAvailable(char letter);
}