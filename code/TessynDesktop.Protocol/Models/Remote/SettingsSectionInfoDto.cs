namespace TessynDesktop.Models.Remote;

/// <summary>A settings section and its version (an in-memory counter, reset when the daemon starts).</summary>
/// <remarks>Created by Claude</remarks>
public sealed record SettingsSectionInfoDto(string Name, long Version);
