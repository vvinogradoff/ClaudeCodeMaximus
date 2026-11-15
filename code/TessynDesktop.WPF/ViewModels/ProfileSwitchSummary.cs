using System.Text;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.WPF.ViewModels;

/// <summary>
/// What a <c>profile.switch</c> did, mapped for the user (ISSUE.013 R1): how many sessions moved now, which running ones are queued, which could not be moved
/// completely. Pure text mapping, no WPF types.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed record ProfileSwitchSummary(
	string ProfileName,
	int MovedCount,
	bool NodeQueued,
	IReadOnlyList<string> QueuedNames,
	IReadOnlyList<string> FailedNames,
	int OldPathCount)
{
	/// <summary>True when there is something the user should read: a queued or failed switch, or paths that still name the old profile.</summary>
	public bool NeedsAttention => NodeQueued || QueuedNames.Count > 0 || FailedNames.Count > 0 || OldPathCount > 0;

	/// <summary>Maps the daemon's answer. <paramref name="nodeId"/> and <paramref name="nodeName"/> are the addressed session: it is in <c>Queued</c> when it was running.</summary>
	public static ProfileSwitchSummary From(ProfileSwitchResultDto result, string nodeId, string nodeName)
	{
		var nodeQueued = result.Queued.Any(q => q.NodeId == nodeId);
		var queued = result.Queued.Where(q => q.NodeId != nodeId).Select(q => q.Name).ToList();
		var failed = result.Failed.Select(f => f.Name).ToList();
		var moved = result.Moved.Count + (result.NodeMoved ? 1 : 0);
		return new ProfileSwitchSummary(result.ProfileName, moved, nodeQueued, queued, failed, result.OldConfigPaths.Count);
	}

	public string ToText(string nodeName)
	{
		var text = new StringBuilder();
		if (MovedCount > 0)
			text.AppendLine(string.Format(Constants.Profiles.ResultMoved, MovedCount, ProfileName));

		if (NodeQueued)
			text.AppendLine(string.Format(Constants.Profiles.ResultNodeQueued, nodeName));

		if (QueuedNames.Count > 0)
		{
			text.AppendLine(Constants.Profiles.ResultQueuedHeader);
			foreach (var name in QueuedNames)
				text.AppendLine("  • " + name);
		}

		if (FailedNames.Count > 0)
		{
			text.AppendLine(Constants.Profiles.ResultFailedHeader);
			foreach (var name in FailedNames)
				text.AppendLine("  • " + name);
		}

		if (OldPathCount > 0)
			text.AppendLine(string.Format(Constants.Profiles.ResultOldPaths, OldPathCount));

		return text.ToString().TrimEnd();
	}
}
