using System;

namespace TessynDesktop.Models.Remote;

/// <summary>Live state of one session as the remote clients see it (contract section 3, shared DTOs).</summary>
/// <param name="Id">The session's NodeId.</param>
/// <param name="Running">A turn is running (headless or interactive).</param>
/// <param name="StreamingAlive">A Persistent Session Process is alive for the session.</param>
/// <param name="Attention">Row badge; omitted when the session has no open attention item.</param>
/// <param name="Favorite">The session is a favourite.</param>
/// <param name="LastActivityUtc">Most recent of last prompt, last response and last non-blank draft edit.</param>
/// <param name="LastPromptUtc">Time of the last USER entry.</param>
/// <param name="HasDraft">The session has a non-blank unsent draft.</param>
/// <param name="ProfileName">Profile display name stamped on the last USER entry.</param>
/// <param name="AuthError">The profile's authentication failed in the last turn of the session and no turn has succeeded since (capability <c>session.authError</c>).</param>
/// <param name="HasSchedules">At least one schedule targets the session (capability <c>schedules</c>).</param>
/// <param name="PinnedProfileId">The profile the session is pinned to (ISSUE.013 R5): <c>"default"</c> or a stored profile's id; omitted while the session is not pinned. Unlike <paramref name="ProfileName"/> it follows a profile switch at once.</param>
/// <param name="PinnedProfileName">The display name of the pinned profile (the <c>profile=</c> label of <c>list_sessions</c>; <c>"default"</c> for the Default profile; a missing profile carries the missing suffix); omitted without a pin.</param>
/// <param name="SwitchingToProfileName">The display name of the profile a queued switch will move the session to when its turn ends (the <c>switching-to=</c> label of <c>list_sessions</c>); omitted when no switch is queued.</param>
/// <param name="Resumable">Claude Code still has the session available for <c>--resume</c> (the blue square of the Avalonia tree); omitted (null) until the daemon's first sweep of the tree has run. Comes from a cached batch sweep (<c>ISessionResumabilityCache</c>), never from a file probe per session.</param>
/// <remarks>Created by Claude</remarks>
public sealed record SessionStateDto(
	string Id,
	bool Running,
	bool StreamingAlive,
	RowAttentionDto? Attention,
	bool Favorite,
	DateTimeOffset? LastActivityUtc,
	DateTimeOffset? LastPromptUtc,
	bool HasDraft,
	string? ProfileName,
	bool AuthError = false,
	bool HasSchedules = false,
	string? PinnedProfileId = null,
	string? PinnedProfileName = null,
	string? SwitchingToProfileName = null,
	bool? Resumable = null);
