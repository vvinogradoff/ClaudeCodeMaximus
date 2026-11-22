using TessynDesktop.Codex;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.Services;

/// <inheritdoc cref="ISessionStateBuilder" />
/// <remarks>Created by Claude</remarks>
public sealed class SessionStateBuilder : ISessionStateBuilder
{
	private readonly ISessionRegistry _registry;
	private readonly ISessionEngineRouter _engines;
	private readonly IAttentionService _attention;
	private readonly ISessionActivityService _activity;
	private readonly ISessionAuthErrorTracker? _authErrors;
	private readonly ISchedulerService? _scheduler;
	private readonly IProfileResolver? _profiles;
	private readonly ISessionResumabilityCache? _resumable;
	private readonly ICodexProfileService? _codexProfiles;
	private readonly CodexProfileNaming _codexNaming = new();

	public SessionStateBuilder(
		ISessionRegistry registry,
		ISessionEngineRouter engines,
		IAttentionService attention,
		ISessionActivityService activity,
		ISessionAuthErrorTracker? authErrors = null,
		ISchedulerService? scheduler = null,
		IProfileResolver? profiles = null,
		ISessionResumabilityCache? resumable = null,
		ICodexProfileService? codexProfiles = null)
	{
		_codexProfiles = codexProfiles;
		_resumable = resumable;
		_profiles  = profiles;
		_scheduler = scheduler;
		_registry  = registry;
		_engines = engines;
		_attention = attention;
		_activity  = activity;
		_authErrors = authErrors;
	}

	/// <inheritdoc />
	public SessionStateDto Build(SessionNodeModel node)
	{
		var nodeId   = node.NodeId;
		var summary  = _attention.GetSummary(nodeId);
		var activity = _activity.GetActivity(node);

		// The row badge counts every open item, Decided included (FR.23.17); no open item means no badge.
		var attention = summary.AllOpenCount > 0 && summary.AllOpenTopKind.HasValue
			? new RowAttentionDto(summary.AllOpenCount, summary.AllOpenTopKind.Value)
			: null;

		var (pinnedId, pinnedName, switchingTo) = DescribeProfile(node);
		var runningWorkers = _registry.CountRunningWorkers(nodeId);

		return new SessionStateDto(
			Id:              nodeId,
			Running:         _registry.IsSessionRunning(nodeId),
			StreamingAlive:  _engines.IsAlive(nodeId),
			Attention:       attention,
			Favorite:        node.IsFavorite,
			LastActivityUtc: activity.LastActivityUtc,
			LastPromptUtc:   activity.LastPromptUtc,
			HasDraft:        activity.HasDraft,
			ProfileName:     activity.LastPromptProfileName,
			AuthError:       _authErrors?.Has(nodeId) ?? false,
			HasSchedules:    _scheduler?.GetSchedules(nodeId).Count > 0,
			PinnedProfileId:        pinnedId,
			PinnedProfileName:      pinnedName,
			SwitchingToProfileName: switchingTo,
			Resumable:             _resumable?.TryGet(nodeId),
			RunningWorkerCount:    runningWorkers > 0 ? runningWorkers : null,
			LimitError:            _authErrors?.GetLimitError(nodeId),
			Engine:                string.IsNullOrEmpty(node.EngineId) ? RemoteProtocol.Engines.Claude : node.EngineId);
	}

	/// <summary>The pin and the queued switch of a session, labelled as <c>list_sessions</c> labels them (ISSUE.013 R5). All null without a resolver or a pin.</summary>
	private (string? PinnedId, string? PinnedName, string? SwitchingTo) DescribeProfile(SessionNodeModel node)
	{
		if (_codexProfiles != null && _codexNaming.IsCodexProfileId(node.ProfileId))
			return DescribeCodexProfile(node);

		if (_profiles == null)
			return (null, null, null);

		var pin = _profiles.DescribePin(node);
		var pinnedName = pin == null ? null : pin.IsResolved ? pin.DisplayName : pin.DisplayName + Constants.ProfileSwitch.MissingProfileSuffix;

		return (pin?.ProfileId, pinnedName, SwitchingTo(node));
	}

	/// <summary>
	/// A ChatGPT pin (FR.33.5): the profile id and <c>ChatGPT · &lt;email&gt;</c>, with the missing suffix when the profile was deleted; a queued
	/// switch to another ChatGPT profile is named the same way. The Claude resolver ignores ChatGPT pins, so they are described here.
	/// </summary>
	private (string? PinnedId, string? PinnedName, string? SwitchingTo) DescribeCodexProfile(SessionNodeModel node)
	{
		var pinnedId = node.ProfileId!;
		var pinnedName = _codexProfiles!.DisplayNameOf(pinnedId) ?? pinnedId + Constants.ProfileSwitch.MissingProfileSuffix;
		return (pinnedId, pinnedName, SwitchingTo(node));
	}

	/// <summary>The display name of the queued switch's target, a Claude or a ChatGPT profile; null when none is queued.</summary>
	private string? SwitchingTo(SessionNodeModel node)
	{
		var target = node.PendingProfileId;
		if (string.IsNullOrEmpty(target))
			return null;

		var name = _codexProfiles != null && _codexNaming.IsCodexProfileId(target) ? _codexProfiles.DisplayNameOf(target) : _profiles?.FindById(target)?.DisplayName;
		return name ?? target + Constants.ProfileSwitch.MissingProfileSuffix;
	}
}
