namespace TessynDesktop.Models.Remote;

/// <summary>Result of <c>settings.get</c>; <typeparamref name="TValues"/> is the section's DTO.</summary>
/// <remarks>Created by Claude</remarks>
public sealed record SettingsGetResult<TValues>(string Section, long Version, TValues Values);
