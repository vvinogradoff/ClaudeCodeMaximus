using TessynDesktop.Codex;
using TessynDesktop.Models;
using TessynDesktop.Models.Turns;

namespace TessynDesktop.Tests.Codex.Engine;

/// <summary>Puts every Codex turn on one profile the test sets (stored profiles come with phase 3).</summary>
/// <remarks>Created by Claude</remarks>
internal sealed class FixedProfileSelector : ICodexProfileSelector
{
	public FixedProfileSelector(CodexProfileRef profile) => Profile = profile;

	public CodexProfileRef Profile { get; set; }

	public CodexProfileRef Select(SessionNodeModel node, TurnSettings? settings) => Profile;
}
