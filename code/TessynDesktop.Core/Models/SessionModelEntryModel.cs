using System.Collections.Generic;

namespace TessynDesktop.Models;

/// <summary>
/// One model of the merged catalog (FR.33.2), as the phase 5 wire will send it: the id, the name, its engine (<c>claude</c> or <c>codex</c>), its
/// provider, the efforts it offers (FR.33.3; for GPT models without <c>ultra</c>, each with Codex's description), its default effort, whether it
/// is Codex's default, its input modalities and Codex's upgrade hint. <see cref="Claude"/> is the Claude catalog entry of a Claude or Ollama model.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed record SessionModelEntryModel(
	string Id,
	string DisplayName,
	string EngineId,
	ModelProvider Provider,
	IReadOnlyList<ModelEffortOptionModel> Efforts,
	string? DefaultEffort,
	bool IsDefault,
	IReadOnlyList<string> InputModalities,
	string? Upgrade,
	ClaudeModelInfo? Claude);
