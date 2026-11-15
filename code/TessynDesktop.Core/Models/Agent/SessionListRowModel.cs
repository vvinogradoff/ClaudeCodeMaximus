using System;

namespace TessynDesktop.Models.Agent;

/// <summary>One row of a listing page (FR.15.13).</summary>
/// <remarks>Created by Claude</remarks>
public sealed record SessionListRowModel
{
	public required string NodeId { get; init; }

	public required string ShortId { get; init; }

	/// <summary>Full name; the formatter truncates.</summary>
	public required string Name { get; init; }

	/// <summary>Null when the session file is missing.</summary>
	public DateTimeOffset? LastActivityUtc { get; init; }

	public bool IsRunning { get; init; }

	/// <summary>The profile the session runs on (<c>profile=</c> field, ISSUE.013 R5).</summary>
	public string ProfileLabel { get; init; } = Constants.ProfileSwitch.DefaultProfileName;

	/// <summary>The profile a queued switch will move the session to; null when none is queued.</summary>
	public string? SwitchingToLabel { get; init; }

	public bool IsFavorite { get; init; }

	public bool IsResumable { get; init; }

	/// <summary>Direct workers anywhere in the tree.</summary>
	public int WorkerCount { get; init; }

	public int RunningWorkerCount { get; init; }

	/// <summary>0 = not nested.</summary>
	public int IndentLevel { get; init; }

	/// <summary>Set when the "worker of &lt;id&gt;" flag applies and the supervisor is not the caller.</summary>
	public string? WorkerOfShortId { get; init; }

	/// <summary>The "worker of you" flag.</summary>
	public bool IsWorkerOfCaller { get; init; }

	/// <summary>Set when the row's Directory Node differs from its section's.</summary>
	public string? OtherDirectoryLabel { get; init; }

	public bool IsCaller { get; init; }

	/// <summary>Index into <see cref="SessionListPageModel.Sections"/>.</summary>
	public required int SectionIndex { get; init; }
}
