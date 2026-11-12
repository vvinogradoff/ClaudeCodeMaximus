using System;

namespace TessynDesktop.Models;

/// <summary>
/// Immutable copy of the fields of one session that the remote modules read, taken under the settings lock so
/// no live model is touched afterwards (except <see cref="Node"/>, which only the state builder reads).
/// </summary>
/// <param name="Node">The live model; hand it to <c>ISessionStateBuilder</c> / <c>ISessionActivityService</c> only.</param>
/// <param name="NodeId">The session's NodeId.</param>
/// <param name="Name">Display name.</param>
/// <param name="SupervisorNodeId">NodeId of the supervisor, or null.</param>
/// <param name="DirectoryId">NodeId of the Directory node that contains the session.</param>
/// <param name="DirectoryPath">Path of that Directory node.</param>
/// <param name="WorkingDirectory">The session's own working directory.</param>
/// <param name="ExternalKey">Search index key: <c>ExternalId ?? ClaudeSessionId</c>; null for a session that never ran.</param>
/// <param name="IsFavorite">Favourite flag.</param>
/// <param name="RecentIgnoredAt">When the session was hidden from the Recent dropdown, or null.</param>
/// <remarks>Created by Claude</remarks>
public sealed record RemoteSessionRecord(
	SessionNodeModel Node,
	string NodeId,
	string Name,
	string? SupervisorNodeId,
	string DirectoryId,
	string DirectoryPath,
	string WorkingDirectory,
	string? ExternalKey,
	bool IsFavorite,
	DateTimeOffset? RecentIgnoredAt);
