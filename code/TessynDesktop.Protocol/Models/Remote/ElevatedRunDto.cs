using System;

namespace TessynDesktop.Models.Remote;

/// <summary>
/// An elevated run request as the wire shows it (tdcp-spec 10.A13). <see cref="Command"/> is always whole and unchanged. Over the relay
/// <see cref="WorkingDir"/> and <see cref="LogPath"/> are omitted and <see cref="WorkingDirName"/> (the last folder name) is sent instead.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed record ElevatedRunDto
{
	public string Id { get; init; } = string.Empty;
	public ElevatedRunStatus Status { get; init; }
	public ElevatedRequesterDto Requester { get; init; } = new(string.Empty, string.Empty, string.Empty);
	public ElevatedTopLevelDto? TopLevel { get; init; }
	public string Reason { get; init; } = string.Empty;
	public string? WorkingDir { get; init; }
	public string? WorkingDirName { get; init; }
	public string Command { get; init; } = string.Empty;
	public int TimeoutMinutes { get; init; }
	public DateTimeOffset CreatedUtc { get; init; }
	public DateTimeOffset ExpiresUtc { get; init; }
	public DateTimeOffset? StartedUtc { get; init; }
	public DateTimeOffset? EndedUtc { get; init; }
	public int? ExitCode { get; init; }
	public ElevatedRunCancelledBy? CancelledBy { get; init; }

	/// <summary>The user's decline note (at most 500 characters).</summary>
	public string? Note { get; init; }

	/// <summary>The failure text (at most 500 characters).</summary>
	public string? Failure { get; init; }

	public string? LogPath { get; init; }
}
