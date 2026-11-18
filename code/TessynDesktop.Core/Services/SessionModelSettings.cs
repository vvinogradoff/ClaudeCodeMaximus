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

	/// <param name="models">The catalog that decides what a client shows (<see cref="ResolveShown"/>); without it every model counts as listed.</param>
	public SessionModelSettings(IAppSettingsService appSettings, IClaudeModelService? models = null)
	{
		_appSettings = appSettings;
		_models      = models;
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
				var picked = KnownEffort(options.Effort);
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

	/// <summary>The projection a client shows: a model in the catalog's form (null when it is not listed), a known effort (null otherwise).</summary>
	private SessionModelSettingsModel Shown(SessionModelSettingsModel settings) =>
		new(ListedForm(settings.ModelId), KnownEffort(settings.Effort));

	/// <summary>The catalog's id for <paramref name="modelId"/> (matched case-insensitively); null for none or an unlisted model. Without a catalog, the id itself.</summary>
	private string? ListedForm(string? modelId)
	{
		if (modelId == null || _models == null)
			return modelId;

		return _models.GetCachedModels().FirstOrDefault(m => string.Equals(m.Id, modelId, StringComparison.OrdinalIgnoreCase))?.Id;
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
