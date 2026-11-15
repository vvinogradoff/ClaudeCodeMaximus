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

	public SessionStateBuilder(
		ISessionRegistry registry,
		IClaudeStreamingSessionManager streaming,
		IAttentionService attention,
		ISessionActivityService activity,
		ISessionAuthErrorTracker? authErrors = null)
	{
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
			AuthError:       _authErrors?.Has(nodeId) ?? false);
	}
}
