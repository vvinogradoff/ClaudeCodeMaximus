namespace TessynDesktop.Models.Remote;

/// <summary>Params of <c>settings.changed</c>.</summary>
/// <remarks>Created by Claude</remarks>
public sealed record SettingsChangedPayload(string Section, long Version);
