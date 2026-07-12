namespace ClaudeMaximus.Services;

/// <summary>
/// Maps model IDs to integer capability tiers and enforces the rule that a session
/// may only spawn workers at or below its own tier (FR.16).
/// </summary>
/// <remarks>Created by Claude</remarks>
public static class ModelTierService
{
	/// <summary>
	/// Returns the capability tier of the given model ID (FR.16.1).
	/// Tier 4 = Fable, 3 = Opus, 2 = Sonnet, 1 = Haiku, 0 = Local/Unknown.
	/// Matching is case-insensitive substring search; highest match wins.
	/// </summary>
	public static int GetTier(string? modelId)
	{
		if (string.IsNullOrEmpty(modelId))
			return Constants.Agent.DefaultModelTier;

		var id = modelId.ToLowerInvariant();

		if (id.Contains("fable"))  return 4;
		if (id.Contains("opus"))   return 3;
		if (id.Contains("sonnet")) return 2;
		if (id.Contains("haiku"))  return 1;

		return 0; // Local / Ollama / unknown
	}

	/// <summary>
	/// Returns true when a session running <paramref name="callerModelId"/> is allowed
	/// to spawn/configure a worker running <paramref name="requestedModelId"/>.
	/// </summary>
	public static bool IsAllowed(string? callerModelId, string? requestedModelId) =>
		GetTier(callerModelId) >= GetTier(requestedModelId);

	/// <summary>Human-readable tier name for error messages.</summary>
	public static string TierName(int tier) => tier switch
	{
		4 => "Fable",
		3 => "Opus",
		2 => "Sonnet",
		1 => "Haiku",
		_ => "Local/Unknown",
	};
}
