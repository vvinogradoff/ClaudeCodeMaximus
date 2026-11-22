using TessynDesktop.Models;
using TessynDesktop.Models.Turns;

namespace TessynDesktop.Codex;

/// <summary>Every Codex session runs on Codex Default (FR.33.5, <c>codex-default</c>, no <c>CODEX_HOME</c>) until stored profiles exist (phase 3).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class CodexDefaultProfileSelector : ICodexProfileSelector
{
	private readonly CodexProfileRef _default = new(Constants.Codex.Profiles.DefaultProfileId, CodexHome: null);

	/// <inheritdoc />
	public CodexProfileRef Select(SessionNodeModel node, TurnSettings? settings) => _default;
}
