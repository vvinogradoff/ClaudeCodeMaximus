using System.Collections.Generic;
using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <summary>
/// The models a session can run on, across engines (FR.33.2): the Anthropic models, then the GPT models Codex reports, then the Ollama models.
/// Each entry carries its engine and its efforts. The daemon decides a turn's engine (FR.33.4), tiers (FR.33.14) and the orchestration checks
/// (FR.33.15) from it; <c>catalog.get</c> will send it in phase 5.
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface ISessionModelCatalog
{
	/// <summary>All models in picker order: Anthropic, GPT (the default model first, then Codex's order), Ollama.</summary>
	IReadOnlyList<SessionModelEntryModel> GetModels();

	/// <summary>The entry with this id (any case); null for <c>default</c>, a blank id or an id in no catalog.</summary>
	SessionModelEntryModel? Find(string? modelId);

	/// <summary>True when the id is a GPT model of the catalog.</summary>
	bool IsGpt(string? modelId);

	/// <summary>
	/// True when the id looks like a GPT model (<c>gpt-</c> prefix, or <c>o</c> followed by a digit) and is not a model of the Anthropic or Ollama
	/// list (FR.33.15: catalog membership comes first). It may or may not be in the GPT list.
	/// </summary>
	bool LooksLikeGpt(string? modelId);

	/// <summary>True once a GPT list with at least one model was loaded (from the cache or Codex); before that no id can be told to be a GPT model.</summary>
	bool IsGptCatalogLoaded { get; }

	/// <summary>The efforts the model offers, in picker order without Default; Claude, Ollama and unknown models get the Claude list.</summary>
	IReadOnlyList<ModelEffortOptionModel> EffortsFor(string? modelId);

	/// <summary>The effort a turn on the model sends: null for none, Default, or an effort the model does not offer (FR.33.3: it stays stored).</summary>
	string? EffectiveEffort(string? modelId, string? effort);

	/// <summary>The GPT ids for an error: the first 12, then <c>(+N more)</c>.</summary>
	string ListGptModels();

	/// <summary>The left status-bar section of a GPT session (FR.33.10): <c>&lt;display name&gt;  ·  ChatGPT plan</c>; null for any other model.</summary>
	string? StatusBarModelLabel(string? modelId);

	/// <summary>The disabled picker line when there are no GPT entries (FR.33.2), else null.</summary>
	string? GptHintLine { get; }
}
