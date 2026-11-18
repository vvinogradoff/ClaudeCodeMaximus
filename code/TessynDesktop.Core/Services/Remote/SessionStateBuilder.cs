using TessynDesktop.Models;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.Services;

/// <inheritdoc cref="ISessionStateBuilder" />
/// <remarks>Created by Claude</remarks>
public sealed class SessionStateBuilder : ISessionStateBuilder
{
	private readonly ISessionRegistry _registry;
	private readonly IClaudeStreamingSessionManager _streaming;
	private readonly IAttentionService _attention;
	private readonly ISessionActivityService _activity;
	private readonly ISessionAuthErrorTracker? _authErrors;
	private readonly ISchedulerService? _scheduler;
	private readonly IProfileResolver? _profiles;
	private readonly ISessionResumabilityCache? _resumable;

	public SessionStateBuilder(
		ISessionRegistry registry,
		IClaudeStreamingSessionManager streaming,
		IAttentionService attention,
		ISessionActivityService activity,
		ISessionAuthErrorTracker? authErrors = null,
		ISchedulerService? scheduler = null,
		IProfileResolver? profiles = null,
		ISessionResumabilityCache? resumable = null)
	{
		_resumable = resumable;
		_profiles  = profiles;
		_scheduler = scheduler;
		_registry  = registry;
		_streaming = streaming;
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
			StreamingAlive:  _streaming.IsAlive(nodeId),
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
			RunningWorkerCount:    runningWorkers > 0 ? runningWorkers : null);
	}

	/// <summary>The pin and the queued switch of a session, labelled as <c>list_sessions</c> labels them (ISSUE.013 R5). All null without a resolver or a pin.</summary>
	private (string? PinnedId, string? PinnedName, string? SwitchingTo) DescribeProfile(SessionNodeModel node)
	{
		if (_profiles == null)
			return (null, null, null);

		var pin = _profiles.DescribePin(node);
		var pinnedName = pin == null ? null : pin.IsResolved ? pin.DisplayName : pin.DisplayName + Constants.ProfileSwitch.MissingProfileSuffix;

		string? switchingTo = null;
		if (!string.IsNullOrEmpty(node.PendingProfileId))
			switchingTo = _profiles.FindById(node.PendingProfileId)?.DisplayName ?? node.PendingProfileId + Constants.ProfileSwitch.MissingProfileSuffix;

		return (pin?.ProfileId, pinnedName, switchingTo);
	}
}
