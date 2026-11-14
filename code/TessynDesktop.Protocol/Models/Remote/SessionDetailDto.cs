using System.Collections.Generic;

namespace TessynDesktop.Models.Remote;

/// <summary>Everything the session screen needs besides the transcript (contract section 3.9).</summary>
/// <param name="Id">The session's NodeId.</param>
/// <param name="Name">The session's name.</param>
/// <param name="DirectoryId">NodeId of the containing directory.</param>
/// <param name="GroupId">NodeId of the immediate containing group; omitted for a session directly under its directory.</param>
/// <param name="Location">Where the session sits in the tree.</param>
/// <param name="SupervisorId">NodeId of the supervising session when it exists.</param>
/// <param name="State">Live state.</param>
/// <param name="Tags">Session and name-derived tags.</param>
/// <param name="Attention">Focus badge inputs.</param>
/// <param name="LastUsed">Settings the session was last used with.</param>
/// <param name="IsStreamingMode">Streaming Session Mode is on.</param>
/// <param name="EntryCount">Number of parsed transcript entries (empty ones included).</param>
/// <param name="Epoch">Transcript epoch (see ISessionEpochTracker).</param>
/// <remarks>Created by Claude</remarks>
public sealed record SessionDetailDto(
	string Id,
	string Name,
	string DirectoryId,
	string? GroupId,
	SessionLocationDto Location,
	string? SupervisorId,
	SessionStateDto State,
	IReadOnlyList<SessionTagDto> Tags,
	AttentionSummaryDto Attention,
	LastUsedSettingsDto LastUsed,
	bool IsStreamingMode,
	int EntryCount,
	int Epoch);
