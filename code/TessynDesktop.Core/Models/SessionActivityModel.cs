using System;

namespace TessynDesktop.Models;

/// <summary>What the daemon knows about a session's recent activity (see <see cref="Services.ISessionActivityService"/>).</summary>
/// <param name="LastActivityUtc">Most recent of the last prompt, last response and last non-blank draft edit.</param>
/// <param name="LastPromptUtc">Timestamp of the last USER entry.</param>
/// <param name="LastResponseUtc">Timestamp of the last ASSISTANT entry.</param>
/// <param name="LastPromptProfileName">Profile display name stamped on the last USER entry.</param>
/// <param name="HasDraft">True while the session has a non-blank unsent draft.</param>
/// <remarks>Created by Claude</remarks>
public sealed record SessionActivityModel(
	DateTimeOffset? LastActivityUtc,
	DateTimeOffset? LastPromptUtc,
	DateTimeOffset? LastResponseUtc,
	string? LastPromptProfileName,
	bool HasDraft);
