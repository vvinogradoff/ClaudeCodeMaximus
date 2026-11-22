using System;
using System.Linq;
using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <inheritdoc cref="ISessionModelSettings" />
/// <remarks>
/// Replaces the three copies of the model chain (headless turn, engine turn, compaction) and adds the session's effort, which before
/// came only from a per-turn pick. Stateless; reads and writes the session under the settings lock. Created by Claude.
/// </remarks>
public sealed class SessionModelSettings : ISessionModelSettings
{
	private readonly IAppSettingsService _appSettings;
	private readonly IClaudeModelService? _models;
	private readonly ISessionModelCatalog? _sessionModels;

	/// <param name="models">The catalog that decides what a client shows (<see cref="ResolveShown"/>); without it every model counts as listed.</param>
	/// <param name="sessionModels">
	/// FR.33.2, FR.33.3: the merged catalog (Claude, GPT, Ollama). With it a GPT model is listed, a shown effort is one the session's model offers
	/// (a GPT model's own list), and a GPT effort such as <c>xhigh</c> can be stored as a pick. It wins over <paramref name="models"/>.
	/// </param>
	public SessionModelSettings(IAppSettingsService appSettings, IClaudeModelService? models = null, ISessionModelCatalog? sessionModels = null)
	{
		_appSettings   = appSettings;
		_models        = models;
		_sessionModels = sessionModels;
	}

	/// <inheritdoc />
	public SessionModelSettingsModel ResolveSession(SessionNodeModel node)
	{
		lock (_appSettings.SyncRoot)
			return ResolveSessionLocked(node);
	}

	/// <inheritdoc />
	public SessionModelSettingsModel ResolveTurn(SessionNodeModel node, TurnOptionsModel? options)
	{
		var pickedModel  = options?.ModelId;
		var pickedEffort = options?.Effort;
		var session = string.IsNullOrEmpty(pickedModel) || string.IsNullOrEmpty(pickedEffort) ? ResolveSession(node) : null;

		return new SessionModelSettingsModel(
			string.IsNullOrEmpty(pickedModel) ? session!.ModelId : NoFlagWhenDefault(pickedModel),
			string.IsNullOrEmpty(pickedEffort) ? session!.Effort : NoFlagWhenDefault(pickedEffort));
	}

	/// <inheritdoc />
	public SessionModelSettingsModel ResolveShown(SessionNodeModel node)
	{
		lock (_appSettings.SyncRoot)
			return Shown(ResolveSessionLocked(node));
	}

	/// <inheritdoc />
	public bool RecordPicks(SessionNodeModel node, TurnOptionsModel? options)
	{
		if (options == null || (string.IsNullOrEmpty(options.ModelId) && string.IsNullOrEmpty(options.Effort)))
			return false;

		lock (_appSettings.SyncRoot)
		{
			// Compared with what the client was shown: a client that sends back the "default" it showed for an unlisted model changes nothing.
			var shown   = Shown(ResolveSessionLocked(node));
			var changed = false;

			if (!string.IsNullOrEmpty(options.ModelId))
			{
				var picked = ListedForm(NoFlagWhenDefault(options.ModelId)) ?? NoFlagWhenDefault(options.ModelId);
				if (!string.Equals(picked, shown.ModelId, StringComparison.OrdinalIgnoreCase))
				{
					// "default" is kept as a value: null would bring back the directory's or app's model.
					node.ModelId = picked ?? Constants.Remote.DefaultOptionId;
					changed      = true;
				}
			}

			if (!string.IsNullOrEmpty(options.Effort))
			{
				var picked = KnownPickedEffort(options.Effort);
				if (!string.Equals(picked, shown.Effort, StringComparison.OrdinalIgnoreCase))
				{
					node.Effort = picked;
					changed     = true;
				}
			}

			if (changed)
				_appSettings.Save();

			return changed;
		}
	}

	private SessionModelSettingsModel ResolveSessionLocked(SessionNodeModel node)
	{
		var model = node.ModelId;
		if (string.IsNullOrEmpty(model))
		{
			var directory = _appSettings.Settings.Tree.FirstOrDefault(d =>
				string.Equals(d.Path, node.WorkingDirectory, StringComparison.OrdinalIgnoreCase));
			model = !string.IsNullOrEmpty(directory?.SelectedModelId) ? directory.SelectedModelId : _appSettings.Settings.SelectedModelId;
		}

		return new SessionModelSettingsModel(NoFlagWhenDefault(model), NoFlagWhenDefault(node.Effort));
	}

	/// <summary>
	/// The projection a client shows: a model in the catalog's form (null when it is not listed), a known effort (null otherwise). With the merged
	/// catalog the effort must be one the session's model offers (FR.33.3: one it does not offer is shown as Default and stays stored).
	/// </summary>
	private SessionModelSettingsModel Shown(SessionModelSettingsModel settings) =>
		new(ListedForm(settings.ModelId), _sessionModels == null ? KnownEffort(settings.Effort) : OfferedEffort(settings.ModelId, settings.Effort));

	/// <summary>The catalog's id for <paramref name="modelId"/> (matched case-insensitively); null for none or an unlisted model. Without a catalog, the id itself.</summary>
	private string? ListedForm(string? modelId)
	{
		if (modelId == null)
			return null;

		if (_sessionModels != null)
			return _sessionModels.Find(modelId)?.Id;

		if (_models == null)
			return modelId;

		return _models.GetCachedModels().FirstOrDefault(m => string.Equals(m.Id, modelId, StringComparison.OrdinalIgnoreCase))?.Id;
	}

	/// <summary>The effort in the form <paramref name="modelId"/> offers it; null for none, <c>"default"</c> or an effort the model does not offer.</summary>
	private string? OfferedEffort(string? modelId, string? effort)
	{
		var value = NoFlagWhenDefault(effort);
		return value == null ? null : _sessionModels!.EffortsFor(modelId).FirstOrDefault(e => string.Equals(e.Effort, value, StringComparison.OrdinalIgnoreCase))?.Effort;
	}

	/// <summary>A picked effort to store: a Claude effort, or (with the merged catalog) one any GPT model offers; null otherwise.</summary>
	private string? KnownPickedEffort(string? effort)
	{
		var known = KnownEffort(effort);
		if (known != null || _sessionModels == null)
			return known;

		var value = NoFlagWhenDefault(effort);
		return value == null ? null : _sessionModels.GetModels().SelectMany(m => m.Efforts).FirstOrDefault(e => string.Equals(e.Effort, value, StringComparison.OrdinalIgnoreCase))?.Effort;
	}

	/// <summary>The effort in its known form; null for none, <c>"default"</c> or an unknown effort.</summary>
	private static string? KnownEffort(string? effort)
	{
		var value = NoFlagWhenDefault(effort);
		return value == null ? null : Constants.Remote.Efforts.FirstOrDefault(e => string.Equals(e, value, StringComparison.OrdinalIgnoreCase));
	}

	/// <summary>Null for a blank value or <c>"default"</c> (no flag); otherwise the value.</summary>
	private static string? NoFlagWhenDefault(string? value) =>
		string.IsNullOrWhiteSpace(value) || string.Equals(value, Constants.Remote.DefaultOptionId, StringComparison.OrdinalIgnoreCase)
			? null
			: value;
}
