using System.Text.Json.Nodes;
using TessynDesktop.Models.Engines;

namespace TessynDesktop.Codex;

/// <summary>
/// What the Codex engine prepares before the turn lock (FR.33.9): the thread's <c>config</c>, with the registry servers' freshly refreshed
/// tokens. It holds the agent token, so <see cref="ToString"/> never prints it.
/// </summary>
/// <remarks>Created by Claude</remarks>
internal sealed record CodexTurnPreparation(JsonObject Config) : EngineTurnPreparation
{
	public override string ToString() => $"{nameof(CodexTurnPreparation)} {{ {nameof(Config)} = {Constants.Codex.Config.Redacted} }}";
}
