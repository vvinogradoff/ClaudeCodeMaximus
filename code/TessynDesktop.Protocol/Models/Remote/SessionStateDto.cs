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
	string? ProfileName);
