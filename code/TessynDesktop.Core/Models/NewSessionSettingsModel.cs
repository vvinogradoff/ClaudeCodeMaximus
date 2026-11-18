namespace TessynDesktop.Models;

/// <summary>The settings a session created by <see cref="Services.ISessionRegistry.CreateTopLevelSession"/> starts with (FR.15.15, FR.12.35).</summary>
/// <param name="ModelId">The session's model (<see cref="SessionNodeModel.ModelId"/>); null = inherit, <c>"default"</c> = no flag.</param>
/// <param name="Effort">The session's effort (<see cref="SessionNodeModel.Effort"/>); null = none.</param>
/// <param name="ProfileId">The profile pin (<see cref="SessionNodeModel.ProfileId"/>); null = not pinned.</param>
/// <param name="ProfileName">The pinned profile's display name (<see cref="SessionNodeModel.ProfileName"/>); null for none.</param>
/// <param name="IsStreamingMode">Streaming Session Mode (FR.19).</param>
/// <param name="PlayCompletionSound">Focus Item Sound (FR.22).</param>
/// <remarks>Created by Claude</remarks>
public sealed record NewSessionSettingsModel(
	string? ModelId,
	string? Effort,
	string? ProfileId,
	string? ProfileName,
	bool IsStreamingMode,
	bool PlayCompletionSound);
