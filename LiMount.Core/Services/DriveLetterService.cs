using System.Diagnostics;
using System.Runtime.Versioning;
using LiMount.Core.Interfaces;

namespace LiMount.Core.Services;

/// <summary>
/// Service for enumerating and managing Windows drive letters.
/// This service is Windows-specific as it manages A-Z drive letters.
/// </summary>
[SupportedOSPlatform("windows")]
public class DriveLetterService : IDriveLetterService
{
    /// <summary>
    /// Gets all drive letters currently in use on the system.
    /// Uses DriveInfo.GetDrives() and also checks for network/subst drives.
    /// </summary>
    /// <summary>
    /// Retrieves the set of drive letters currently in use on the system.
    /// </summary>
    /// <remarks>
    /// Enumeration errors are caught and ignored; if drive enumeration fails, the method returns whatever letters were collected before the failure.
    /// </remarks>
    /// <returns>An ascending-sorted list of uppercase drive letters (A–Z) that are currently in use.</returns>
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
    /// <summary>
    /// Enumerates drive letters that are not currently in use.
    /// </summary>
    /// <returns>A list of available drive letters (A–Z) sorted from 'Z' to 'A'.</returns>
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
    /// <summary>
    /// Determine whether a drive letter is available (not currently assigned to a mounted drive).
    /// </summary>
    /// <param name="letter">The drive letter to check; case is ignored.</param>
    /// <returns>`true` if the letter is between 'A' and 'Z' and not currently in use, `false` otherwise.</returns>
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