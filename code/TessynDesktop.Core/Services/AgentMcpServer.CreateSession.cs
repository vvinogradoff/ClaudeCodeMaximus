using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <summary>
/// The <c>create_session</c> tool (FR.15.15): a session creates a top-level session for the user, with its settings and at a given place,
/// unlike <c>spawn_session</c>, which makes a worker of the caller.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class AgentMcpServer
{
	// FR.15.15 rate cap: when the last successful creations happened, across the app (in memory).
	private readonly object _createdSessionsLock = new();
	private readonly List<DateTimeOffset> _createdSessionTimes = [];

	private Task<JsonNode> CreateSessionAsync(JsonNode? args, SessionNodeModel callerNode)
	{
		// A worker reports to its supervisor; only the session the user talks to creates the user's sessions.
		if (callerNode.OrchestrationDepth > 0)
			throw new McpToolException(Constants.CreateSession.WorkerRefusalText);

		var name = GetOptionalString(args, "name")?.Trim()
		           ?? throw new McpToolException(Constants.CreateSession.NameRequiredText);
		if (name.Length == 0)
			throw new McpToolException(Constants.CreateSession.BlankNameText);

		var after      = GetOptionalString(args, "after")?.Trim();
		var workingDir = GetOptionalString(args, "workingDir")?.Trim();
		var group      = GetOptionalString(args, "group")?.Trim();
		var prompt     = GetOptionalString(args, "prompt");
		var streaming  = GetOptionalBool(args, "streaming") ?? false;
		var sound      = GetOptionalBool(args, "sound") ?? true;

		var hasAnchor    = !string.IsNullOrEmpty(after);
		var hasContainer = !string.IsNullOrEmpty(workingDir) || !string.IsNullOrEmpty(group);
		if (hasAnchor && hasContainer)
			throw new McpToolException(Constants.CreateSession.PlacementConflictText);

		// Right after the given session; else last in the given container; else right after the caller.
		var anchor       = hasAnchor ? ResolveSession(after!, Constants.CreateSession.AnchorNotFoundText) : hasContainer ? null : callerNode;
		var containerDir = hasContainer ? (string.IsNullOrEmpty(workingDir) ? callerNode.WorkingDirectory : workingDir) : null;

		var modelId = ValidateCreatedSessionModel(GetOptionalString(args, "model")?.Trim());
		var effort  = ValidateCreatedSessionEffort(GetOptionalString(args, "effort")?.Trim());
		if (modelId != null && modelId != Constants.Remote.DefaultOptionId)
			EnforceModelTier(callerNode, modelId, Constants.CreateSession.TierViolationFormat);

		var pin = ResolveCreatedSessionProfile(GetOptionalString(args, "profile")?.Trim(), callerNode);

		if (!TryReserveCreatedSession(out var stamp))
			throw new McpToolException(string.Format(Constants.CreateSession.RateCapFormat, Constants.Agent.MaxCreatedSessionsPerHour));

		SessionPlacementModel? placed;
		try
		{
			placed = _registry.CreateTopLevelSession(
				new NewSessionPlacementModel(anchor?.NodeId, containerDir, string.IsNullOrEmpty(group) ? null : group),
				name,
				new NewSessionSettingsModel(modelId, effort, pin.ProfileId, pin.ProfileName, streaming, sound));
		}
		catch
		{
			ReleaseCreatedSession(stamp);
			throw;
		}

		if (placed == null)
		{
			// The anchor left the tree since it was resolved, or the directory is not a project.
			ReleaseCreatedSession(stamp);
			throw new McpToolException(anchor != null
				? $"{Constants.CreateSession.AnchorNotFoundText}: {anchor.NodeId}"
				: BuildNotUnderProjectMessage(containerDir ?? string.Empty));
		}

		_log.Information("create_session: {Caller} created {NodeId} '{Name}' (after {Anchor}, in {Directory})",
			callerNode.NodeId, placed.Session.NodeId, name, anchor?.NodeId ?? "-", placed.Directory.Path);

		var started = StartCreatedSessionTurn(placed.Session, prompt);
		return Task.FromResult(BuildTextContent(BuildCreatedSessionText(placed, anchor != null, pin, started)));
	}

	/// <summary>The model to store: null when none was given (the session inherits), <c>"default"</c> as is, else the catalog's id.</summary>
	private string? ValidateCreatedSessionModel(string? model)
	{
		if (string.IsNullOrEmpty(model))
			return null;
		if (string.Equals(model, Constants.Remote.DefaultOptionId, StringComparison.OrdinalIgnoreCase))
			return Constants.Remote.DefaultOptionId;

		var models = _modelService.GetCachedModels();
		var match  = models.FirstOrDefault(m => string.Equals(m.Id, model, StringComparison.OrdinalIgnoreCase));
		if (match != null)
			return match.Id;

		var listed = string.Join(", ", models.Take(Constants.CreateSession.MaxListedModels).Select(m => m.Id))
		             + (models.Count > Constants.CreateSession.MaxListedModels
			             ? string.Format(Constants.CreateSession.MoreModelsFormat, models.Count - Constants.CreateSession.MaxListedModels)
			             : string.Empty);
		throw new McpToolException(string.Format(Constants.CreateSession.UnknownModelFormat, model, listed));
	}

	/// <summary>The effort to store: null for none or <c>"default"</c>, else the known effort.</summary>
	private static string? ValidateCreatedSessionEffort(string? effort)
	{
		if (string.IsNullOrEmpty(effort))
			return null;

		var known = Constants.Remote.Efforts.FirstOrDefault(e => string.Equals(e, effort, StringComparison.OrdinalIgnoreCase))
		            ?? throw new McpToolException(string.Format(Constants.CreateSession.UnknownEffortFormat, effort, string.Join(", ", Constants.Remote.Efforts)));

		return known == Constants.Remote.DefaultOptionId ? null : known;
	}

	/// <summary>
	/// The profile pin of the new session: the given profile (<c>default</c>, a <c>ProfileId</c> or a display name), else the caller's, as a spawned
	/// worker inherits it. A signed-out stored profile is refused: every turn of the new session would fail.
	/// </summary>
	private ProfilePin ResolveCreatedSessionProfile(string? profile, SessionNodeModel callerNode)
	{
		if (string.IsNullOrEmpty(profile))
		{
			var inherited = InheritedProfilePin(callerNode);
			if (inherited.ProfileId != null && _profiles?.FindById(inherited.ProfileId, checkLogin: true) is { IsLoggedOut: true } signedOut)
				throw new McpToolException(string.Format(Constants.CreateSession.LoggedOutProfileFormat, signedOut.DisplayName));
			return inherited;
		}

		if (_profiles == null)
			throw new McpToolException(Constants.CreateSession.NoProfilesText);

		var resolution = _profiles.FindById(profile, checkLogin: true) ?? _profiles.FindByName(profile, checkLogin: true);
		if (resolution is not { IsResolved: true })
			throw new McpToolException(string.Format(Constants.CreateSession.UnknownProfileFormat, profile, ListProfileNames()));
		if (resolution.IsLoggedOut)
			throw new McpToolException(string.Format(Constants.CreateSession.LoggedOutProfileFormat, resolution.DisplayName));

		return new ProfilePin(resolution.ProfileId, resolution.IsDefault ? null : resolution.DisplayName);
	}

	/// <summary><c>default</c> and every stored profile's display name, for an unknown-profile error.</summary>
	private string ListProfileNames()
	{
		lock (_appSettings.SyncRoot)
			return string.Join(", ", new[] { Constants.ProfileSwitch.DefaultProfileId }.Concat(_appSettings.Settings.Profiles.Select(p => p.DisplayName)));
	}

	/// <summary>Takes one slot of the hourly cap, or answers false when the cap is reached. A slot is given back when the creation fails.</summary>
	private bool TryReserveCreatedSession(out DateTimeOffset stamp)
	{
		lock (_createdSessionsLock)
		{
			var now = _clock.GetUtcNow();
			_createdSessionTimes.RemoveAll(t => now - t >= Constants.Agent.CreatedSessionsWindow);

			if (_createdSessionTimes.Count >= Constants.Agent.MaxCreatedSessionsPerHour)
			{
				stamp = default;
				return false;
			}

			_createdSessionTimes.Add(now);
			stamp = now;
			return true;
		}
	}

	private void ReleaseCreatedSession(DateTimeOffset stamp)
	{
		lock (_createdSessionsLock)
			_createdSessionTimes.Remove(stamp);
	}

	/// <summary>
	/// Starts the first turn in the background and returns at once: the new session belongs to the user, so its result stays there (no Mailbox Delivery).
	/// The turn carries no per-turn options, so the session's own model, effort and profile apply.
	/// </summary>
	private bool StartCreatedSessionTurn(SessionNodeModel session, string? prompt)
	{
		if (string.IsNullOrWhiteSpace(prompt))
			return false;

		_ = Task.Run(async () =>
		{
			try
			{
				await _turnService.Value.RunTurnAsync(session, prompt, TurnSource.Orchestrated);
			}
			catch (Exception ex)
			{
				_log.Warning(ex, "create_session: the first turn of {NodeId} failed", session.NodeId);
			}
		});
		return true;
	}

	/// <summary>The result: where the session went, the settings it runs with, and whether its first turn started (FR.15.15).</summary>
	private string BuildCreatedSessionText(SessionPlacementModel placed, bool afterAnchor, ProfilePin pin, bool started)
	{
		var session  = placed.Session;
		var location = _registry.GetSessionLocation(session);
		var container = string.IsNullOrEmpty(location.treePath)
			? location.directoryLabel
			: location.directoryLabel + Constants.CreateSession.PathSeparator + location.treePath;

		// The session before it is the one the registry saw when it inserted, so a concurrent move cannot change the name.
		var place = afterAnchor && placed.PrecedingSession != null
			? string.Format(Constants.CreateSession.PlaceAfterFormat, container, _text.FormatName(placed.PrecedingSession.Name))
			: string.Format(Constants.CreateSession.PlaceLastFormat, container);

		var runsWith = _modelSettings.ResolveSession(session);
		var profile  = pin.ProfileId == Constants.ProfileSwitch.DefaultProfileId || (pin.ProfileId == null && pin.ProfileName == null)
			? Constants.ProfileSwitch.DefaultProfileId
			: pin.ProfileName ?? pin.ProfileId!;

		return string.Format(Constants.CreateSession.ResultFormat, session.Name, session.NodeId, place) + "\n"
		       + string.Format(Constants.CreateSession.SettingsFormat,
			       runsWith.ModelId ?? Constants.Remote.DefaultOptionId,
			       runsWith.Effort ?? Constants.Remote.DefaultOptionId,
			       profile,
			       session.IsStreamingMode ? Constants.CreateSession.On : Constants.CreateSession.Off,
			       session.PlayCompletionSound ? Constants.CreateSession.On : Constants.CreateSession.Off) + "\n"
		       + (started ? Constants.CreateSession.FirstTurnStarted : Constants.CreateSession.NoPromptRun);
	}
}
