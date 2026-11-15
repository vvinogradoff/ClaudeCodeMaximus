namespace TessynDesktop.Models.Remote;

/// <summary>Result of <c>settings.update</c>: the section's version after the update (unchanged when the patch changed nothing).</summary>
/// <remarks>Created by Claude</remarks>
public sealed record SettingsUpdateResult(long Version);
