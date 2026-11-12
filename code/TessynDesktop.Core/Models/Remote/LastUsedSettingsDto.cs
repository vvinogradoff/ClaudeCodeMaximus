namespace TessynDesktop.Models.Remote;

/// <summary>The model, effort and profile a session was last used with, as the desktop restores them when the session is opened.</summary>
/// <param name="ModelId">"default" or a model id.</param>
/// <param name="Effort">"default", "max", "high", "medium" or "low".</param>
/// <param name="ProfileId">"default" or a stored profile's id.</param>
/// <param name="ProfileName">The profile's display name (the "[Sys] ..." label for Default).</param>
/// <remarks>Created by Claude</remarks>
public sealed record LastUsedSettingsDto(string ModelId, string Effort, string ProfileId, string ProfileName);
