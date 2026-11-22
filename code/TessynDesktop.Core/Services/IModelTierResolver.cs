namespace TessynDesktop.Services;

/// <summary>
/// Model tiers across engines (FR.16, FR.33.14): a GPT model's tier comes from the GPT tier table in the settings, every other model keeps the
/// substring rule of <see cref="ModelTierService"/>. Every FR.16 check (spawn, set_session_model, orchestrate_*, create_session) uses it.
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface IModelTierResolver
{
	int GetTier(string? modelId);

	/// <summary>True when a caller on <paramref name="callerModelId"/> may start or configure a worker on <paramref name="requestedModelId"/>.</summary>
	bool IsAllowed(string? callerModelId, string? requestedModelId);

	string TierName(int tier);
}
