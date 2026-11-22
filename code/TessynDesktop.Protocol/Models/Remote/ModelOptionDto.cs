using System.Collections.Generic;

namespace TessynDesktop.Models.Remote;

/// <summary>A model the client may pick (contract section 3.20; the last four members are additive, tdcp-spec 10.A14).</summary>
/// <param name="Id">"default" or a model id.</param>
/// <param name="DisplayName">Name to show.</param>
/// <param name="Provider">Where the model runs (<c>anthropic</c>, <c>ollama</c>, <c>openAI</c>); omitted for the "default" entry.</param>
/// <param name="Engine">The session engine a turn on the model runs on: <c>claude</c> (also "default", Anthropic and Ollama models) or <c>codex</c> (GPT models). An older daemon sends none, which reads as <c>claude</c>.</param>
/// <param name="Efforts">The efforts the model offers, in picker order, without <c>default</c> (FR.33.3); for a GPT model Codex's list without <c>ultra</c>, with descriptions. Omitted by an older daemon: use the global list then.</param>
/// <param name="IsDefault">True for Codex's default GPT model; omitted otherwise.</param>
/// <param name="Upgrade">Codex's upgrade hint for a GPT model (shown as "Codex suggests &lt;upgrade&gt;"); omitted when none.</param>
/// <remarks>Created by Claude</remarks>
public sealed record ModelOptionDto(
	string Id,
	string DisplayName,
	ModelProvider? Provider,
	string Engine = RemoteProtocol.Engines.Claude,
	IReadOnlyList<ModelEffortDto>? Efforts = null,
	bool? IsDefault = null,
	string? Upgrade = null);
