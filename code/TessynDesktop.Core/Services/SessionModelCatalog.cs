using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using TessynDesktop.Codex;
using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <inheritdoc cref="ISessionModelCatalog" />
/// <remarks>
/// Stateless over the two sources: <see cref="IClaudeModelService"/> (Anthropic and Ollama, tagged <c>claude</c>) and <see cref="ICodexModelCatalog"/>
/// (tagged <c>codex</c>, provider OpenAI). Created by Claude.
/// </remarks>
public sealed partial class SessionModelCatalog : ISessionModelCatalog
{
	private readonly IClaudeModelService _claude;
	private readonly ICodexModelCatalog? _codex;

	public SessionModelCatalog(IClaudeModelService claude, ICodexModelCatalog? codex = null)
	{
		_claude = claude;
		_codex  = codex;
	}

	/// <inheritdoc />
	public bool IsGptCatalogLoaded => _codex?.IsLoaded ?? false;

	/// <inheritdoc />
	public string? GptHintLine => (_codex?.Models.Count ?? 0) == 0 ? Constants.Codex.Catalog.SignInHint : null;

	/// <inheritdoc />
	public IReadOnlyList<SessionModelEntryModel> GetModels()
	{
		var claude = _claude.GetCachedModels();
		var gpt    = (_codex?.Models ?? []).Select(Gpt);
		return claude.Where(m => m.Provider != ModelProvider.Ollama).Select(Claude)
			.Concat(gpt)
			.Concat(claude.Where(m => m.Provider == ModelProvider.Ollama).Select(Claude))
			.ToList();
	}

	/// <inheritdoc />
	public SessionModelEntryModel? Find(string? modelId)
	{
		if (string.IsNullOrWhiteSpace(modelId) || string.Equals(modelId, Constants.Remote.DefaultOptionId, StringComparison.OrdinalIgnoreCase))
			return null;

		if (_codex?.Find(modelId) is { } gpt)
			return Gpt(gpt);

		var claude = _claude.GetCachedModels().FirstOrDefault(m =>
			string.Equals(m.Id, modelId, StringComparison.OrdinalIgnoreCase) || (!string.IsNullOrEmpty(m.Alias) && string.Equals(m.Alias, modelId, StringComparison.OrdinalIgnoreCase)));
		return claude is null ? null : Claude(claude);
	}

	/// <inheritdoc />
	public string? StatusBarModelLabel(string? modelId) =>
		_codex?.Find(modelId) is { } gpt ? string.Format(Constants.Codex.Usage.ModelLabelFormat, gpt.DisplayName) : null;

	/// <inheritdoc />
	public bool IsGpt(string? modelId) => _codex?.Find(modelId) is not null;

	/// <inheritdoc />
	public bool LooksLikeGpt(string? modelId) =>
		!string.IsNullOrWhiteSpace(modelId)
		&& (modelId.StartsWith(Constants.Codex.Catalog.GptPrefix, StringComparison.OrdinalIgnoreCase) || OSeriesPattern().IsMatch(modelId));

	/// <inheritdoc />
	public IReadOnlyList<ModelEffortOptionModel> EffortsFor(string? modelId) =>
		_codex?.Find(modelId) is { } gpt ? gpt.Efforts : ClaudeEfforts();

	/// <inheritdoc />
	public string? EffectiveEffort(string? modelId, string? effort)
	{
		if (string.IsNullOrWhiteSpace(effort) || string.Equals(effort, Constants.Remote.DefaultOptionId, StringComparison.OrdinalIgnoreCase))
			return null;

		var offered = EffortsFor(modelId).FirstOrDefault(e => string.Equals(e.Effort, effort, StringComparison.OrdinalIgnoreCase));
		return offered?.Effort;
	}

	/// <inheritdoc />
	public string ListGptModels()
	{
		var ids = (_codex?.Models ?? []).Select(m => m.Id).ToList();
		if (ids.Count == 0)
			return Constants.Codex.Catalog.NoGptModels;

		var listed = string.Join(", ", ids.Take(Constants.Codex.Catalog.MaxListed));
		return ids.Count > Constants.Codex.Catalog.MaxListed
			? listed + string.Format(Constants.Codex.Catalog.MoreFormat, ids.Count - Constants.Codex.Catalog.MaxListed)
			: listed;
	}

	private SessionModelEntryModel Claude(ClaudeModelInfo model) =>
		new(model.Id, model.DisplayName, Constants.SessionEngines.Claude, model.Provider, ClaudeEfforts(), null, false,
			Constants.Codex.Catalog.DefaultInputModalities, null, model);

	private SessionModelEntryModel Gpt(CodexCatalogModel model) =>
		new(model.Id, model.DisplayName, Constants.SessionEngines.Codex, ModelProvider.OpenAI, model.Efforts, model.DefaultEffort, model.IsDefault,
			model.InputModalities, model.Upgrade, null);

	/// <summary>The Claude efforts (FR.12.12) without Default.</summary>
	private IReadOnlyList<ModelEffortOptionModel> ClaudeEfforts() =>
		[.. Constants.Remote.Efforts.Where(e => e != Constants.Remote.DefaultOptionId).Select(e => new ModelEffortOptionModel(e, null))];

	/// <summary><c>o</c> followed by a digit, the OpenAI o-series (FR.33.15).</summary>
	[GeneratedRegex(@"^o\d", RegexOptions.IgnoreCase)]
	private static partial Regex OSeriesPattern();
}
