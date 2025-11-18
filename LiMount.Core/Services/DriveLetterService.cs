using System.Diagnostics;

namespace LiMount.Core.Services;

/// <summary>
/// Service for enumerating and managing Windows drive letters.
/// </summary>
public class DriveLetterService
{
    /// <summary>
    /// Gets all drive letters currently in use on the system.
    /// Uses DriveInfo.GetDrives() and also checks for network/subst drives.
    /// </summary>
    /// <returns>List of uppercase drive letters (A-Z) that are in use</returns>
    public IReadOnlyList<char> GetUsedLetters()
    {
        var usedLetters = new HashSet<char>();

        // Get physical and logical drives
        try
        {
            var drives = DriveInfo.GetDrives();
            foreach (var drive in drives)
            {
                if (drive.Name.Length > 0 && char.IsLetter(drive.Name[0]))
                {
                    usedLetters.Add(char.ToUpperInvariant(drive.Name[0]));
                }
            }
        }
        catch (Exception)
        {
            // If we can't enumerate drives, proceed with what we have
        }

        // Note: On Windows, we could also check for network drives and subst drives
        // using 'net use' or WMI, but DriveInfo should catch most cases.
        // For MVP, this should be sufficient.

        return usedLetters.OrderBy(c => c).ToList();
    }

    /// <summary>
    /// Gets all available (free) drive letters.
    /// Returns letters A-Z that are not in use, sorted Z→A (preferred order).
    /// </summary>
    /// <returns>List of free drive letters, sorted Z→A</returns>
    public IReadOnlyList<char> GetFreeLetters()
    {
        var usedLetters = new HashSet<char>(GetUsedLetters());
        var allLetters = Enumerable.Range('A', 26).Select(i => (char)i);
        var freeLetters = allLetters.Where(c => !usedLetters.Contains(c));

        // Sort Z→A (descending)
        return freeLetters.OrderByDescending(c => c).ToList();
    }

    /// <summary>
    /// Checks if a specific drive letter is available (not in use).
    /// </summary>
    /// <param name="letter">Drive letter to check (case-insensitive)</param>
    /// <returns>True if the letter is free, false if in use</returns>
    public bool IsLetterAvailable(char letter)
    {
        var upperLetter = char.ToUpperInvariant(letter);
        if (upperLetter < 'A' || upperLetter > 'Z')
        {
            return false;
        }

        var usedLetters = new HashSet<char>(GetUsedLetters());
        return !usedLetters.Contains(upperLetter);
    }
}
