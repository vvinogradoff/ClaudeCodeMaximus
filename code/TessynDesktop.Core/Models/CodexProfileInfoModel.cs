namespace TessynDesktop.Models;

/// <summary>
/// A ChatGPT profile as Settings > Profiles shows it (FR.33.5, FR.33.17; the phase 5 wire): its id, its <c>ChatGPT · &lt;email&gt;</c> name, the
/// plan, the folder, and its sign-in and auth-error state.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed record CodexProfileInfoModel(
	string ProfileId,
	string DisplayName,
	string? Email,
	string? PlanType,
	string Home,
	bool IsDefault,
	bool IsKnownSignedIn,
	bool HasAuthError);
