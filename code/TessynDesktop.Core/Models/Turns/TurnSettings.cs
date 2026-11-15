using TessynDesktop.Models;

namespace TessynDesktop.Models.Turns;

/// <summary>
/// The launch settings one turn runs with: what the command bar had selected for an interactive send, or what the daemon
/// resolved for a turn requested over the wire. The engine never reads selections itself.
/// </summary>
/// <param name="ModelId">The <c>--model</c> value; null = no flag.</param>
/// <param name="Effort">The <c>--effort</c> value; null = no flag.</param>
/// <param name="ProfileConfigDir">The credential store of the profile the turn runs under; null = Default.</param>
/// <param name="ProfileDisplayName">Stamped on the turn's USER entry so "last used profile" restores later.</param>
/// <param name="OllamaBaseUrl">Set when the model is served by a local Ollama; routes the process there.</param>
/// <param name="DisableTools">True for a local model without tool support (<c>--tools ""</c>, no MCP config).</param>
/// <param name="Chrome">The experimental <c>--chrome</c> toggle.</param>
/// <param name="ModelInfo">The selected model's catalog entry (input of the Local Tool Profile, FR.12.21).</param>
/// <param name="DirectoryModel">The session's directory (carries its Local Tool Profile).</param>
/// <remarks>Created by Claude</remarks>
public sealed record TurnSettings(
	string? ModelId,
	string? Effort,
	string? ProfileConfigDir,
	string? ProfileDisplayName,
	string? OllamaBaseUrl,
	bool DisableTools,
	bool Chrome,
	ClaudeModelInfo? ModelInfo,
	DirectoryNodeModel? DirectoryModel);
