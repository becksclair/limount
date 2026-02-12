using LiMount.Core.Models;

namespace LiMount.Core.Interfaces;

/// <summary>
/// Persists and retrieves user-level LiMount settings.
/// </summary>
public interface IUserSettingsService
{
    /// <summary>
    /// Loads current settings, or returns defaults when settings are missing/corrupt.
    /// </summary>
    Task<UserSettings> LoadOrCreateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves user settings to durable storage.
    /// </summary>
    Task SaveAsync(UserSettings settings, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resets persisted settings.
    /// </summary>
    Task ResetAsync(CancellationToken cancellationToken = default);
}
