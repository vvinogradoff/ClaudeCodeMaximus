namespace ClaudeMaximus.Models;

/// <summary>
/// Represents a Claude CLI profile with separate authentication context.
/// Each profile uses a dedicated CLAUDE_CONFIG_DIR directory for isolated auth state.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class ClaudeProfileModel
{
	/// <summary>Short profile identifier used as the config subdirectory name (e.g. "profile_1").</summary>
	public string ProfileId { get; set; } = string.Empty;

	/// <summary>Display name shown in the dropdown (typically the account email).</summary>
	public string DisplayName { get; set; } = string.Empty;
}
