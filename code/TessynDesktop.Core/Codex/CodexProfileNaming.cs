using System;

namespace TessynDesktop.Codex;

/// <summary>Tells ChatGPT profile ids and names from Claude ones (FR.33.5): <c>codex-default</c>, <c>codex_N</c> and <c>ChatGPT · &lt;email&gt;</c>.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class CodexProfileNaming
{
	private readonly string _displayPrefix = string.Format(Constants.Codex.Profiles.DisplayNameFormat, string.Empty);

	/// <summary>True for <c>codex-default</c> and the stored ids <c>codex_N</c>.</summary>
	public bool IsCodexProfileId(string? profileId) =>
		!string.IsNullOrEmpty(profileId)
		&& (string.Equals(profileId, Constants.Codex.Profiles.DefaultProfileId, StringComparison.OrdinalIgnoreCase)
		    || profileId.StartsWith(Constants.Codex.Profiles.StoredIdPrefix, StringComparison.OrdinalIgnoreCase));

	/// <summary>True for a name shown as <c>ChatGPT · …</c>.</summary>
	public bool IsChatGptName(string? name) =>
		!string.IsNullOrEmpty(name) && name.StartsWith(_displayPrefix, StringComparison.Ordinal);

	/// <summary>True for either: a pin or a recorded name that belongs to the Codex engine.</summary>
	public bool IsCodexProfile(string? idOrName) => IsCodexProfileId(idOrName) || IsChatGptName(idOrName);

	public string DisplayName(string email) => string.Format(Constants.Codex.Profiles.DisplayNameFormat, email);
}
