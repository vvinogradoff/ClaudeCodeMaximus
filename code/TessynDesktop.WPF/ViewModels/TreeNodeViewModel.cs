using CommunityToolkit.Mvvm.ComponentModel;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.WPF.ViewModels;

/// <summary>One node of the session tree: a directory, group, top-level session or worker session.</summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class TreeNodeViewModel : ObservableObject
{
	/// <summary>Width of one indent level in device-independent pixels.</summary>
	public const double IndentPerLevel = 14;

	[ObservableProperty]
	private string _label;

	[ObservableProperty]
	private bool _isExpanded;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(IsRunning), nameof(IsFavorite), nameof(AttentionCount), nameof(HasAttention), nameof(AttentionKey), nameof(HasDraft), nameof(IsResumable), nameof(LastPromptText), nameof(RecencyLevel), nameof(IsRecentlyActive), nameof(HasSchedules), nameof(ProfileName), nameof(HasProfile), nameof(SwitchingToProfileName), nameof(HasSwitching), nameof(ShowProfileBadge), nameof(HasProfileMismatch), nameof(ProfileToolTip), nameof(RunningWorkerCount), nameof(HasRunningWorkers), nameof(RunningWorkersToolTip))]
	private SessionStateDto? _state;

	[ObservableProperty]
	private bool _isLoadingWorkers;

	[ObservableProperty]
	private string? _matchSummary;

	/// <summary>The git remote origin of the directory that owns the node, from <c>tree.nodeInfo</c>; null when unknown or none.</summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(GitOriginLabel), nameof(HasGitOrigin))]
	private string? _gitOrigin;

	/// <summary>The tooltip: the label until <c>tree.nodeInfo</c> answered, then the path on disk and the origin.</summary>
	[ObservableProperty]
	private string _toolTipText;

	/// <summary>Feedback while a session is dragged over this row.</summary>
	[ObservableProperty]
	private DropFeedback _dropFeedback;

	public TreeNodeViewModel(string id, TreeNodeKind kind, string label, TreeNodeViewModel? parent, int workerCount = 0)
	{
		Id = id;
		Kind = kind;
		_label = label;
		_toolTipText = label;
		Parent = parent;
		Depth = parent == null ? 0 : parent.Depth + 1;
		WorkerCount = workerCount;
	}

	public string Id { get; }

	public TreeNodeKind Kind { get; }

	public TreeNodeViewModel? Parent { get; }

	public int Depth { get; }

	public double IndentWidth => Depth * IndentPerLevel;

	/// <summary>Direct children in tree order: groups and sessions of a directory or group; loaded workers of a session.</summary>
	public List<TreeNodeViewModel> Children { get; } = [];

	/// <summary>Number of worker sessions below a session, as reported by the daemon.</summary>
	public int WorkerCount { get; set; }

	/// <summary>True once <c>tree.workers</c> answered for this session.</summary>
	public bool WorkersLoaded { get; set; }

	/// <summary>For a search-result row: only these workers matched and are shown under it.</summary>
	public IReadOnlySet<string>? WorkerFilter { get; init; }

	public bool IsSessionLike => Kind is TreeNodeKind.Session or TreeNodeKind.Worker;

	/// <summary>
	/// A group that holds nothing to show: the daemon lists the group an orchestration spawned its workers into, but not the workers (they nest under their supervisor), and flags
	/// it (<c>TreeGroupDto.HoldsOnlyWorkers</c>). Husks are not rows of the tree, as in the Avalonia client.
	/// </summary>
	public bool IsHusk { get; set; }

	/// <summary>How many workers a session supervises, written out ("104 workers"): shown inside the session's row. This is a count, not an age; empty for a session without workers.</summary>
	public string WorkerCountText => IsSessionLike && WorkerCount > 0
		? string.Format(Constants.SessionLists.WorkerCountFormat, WorkerCount, WorkerCount == 1 ? Constants.SessionLists.WorkerNoun : Constants.SessionLists.WorkersNoun)
		: string.Empty;

	public bool HasWorkers => WorkerCountText.Length > 0;

	/// <summary>How many of the session's workers (direct and nested) are running right now, from its state; 0 for directories and groups. The red square badge of the row.</summary>
	public int RunningWorkerCount => IsSessionLike ? State?.RunningWorkerCount ?? 0 : 0;

	/// <summary>The badge shows only while a worker runs.</summary>
	public bool HasRunningWorkers => RunningWorkerCount > 0;

	public string RunningWorkersToolTip => string.Format(Constants.SessionLists.RunningWorkersTip, RunningWorkerCount);

	/// <summary>Whether the row shows an expander.</summary>
	public bool CanExpand => IsSessionLike ? WorkerCount > 0 : Children.Any(c => !c.IsHusk);

	/// <summary>True once <c>tree.nodeInfo</c> answered for this node (so a tooltip needs no second call).</summary>
	public bool InfoLoaded { get; set; }

	public bool HasGitOrigin => !string.IsNullOrEmpty(GitOrigin);

	/// <summary>The short form of the git origin shown beside a directory (<c>owner/repo</c>); empty without one.</summary>
	public string GitOriginLabel => GitOrigin == null ? string.Empty : GitOriginText.Short(GitOrigin);

	public bool IsRunning => State?.Running == true;

	/// <summary>The session has an unsent draft (the pencil by its name).</summary>
	public bool HasDraft => IsSessionLike && State?.HasDraft == true;

	/// <summary>Claude Code still has the session for <c>--resume</c> (the lower blue square, as the Avalonia client's <c>IsResumable</c>).</summary>
	public bool IsResumable => IsSessionLike && State?.Resumable == true;

	/// <summary>Date of the last prompt in the format of the Appearance setting; empty for directories and groups and without a prompt.</summary>
	public string LastPromptText => IsSessionLike ? SessionDisplay.DateText(State?.LastPromptUtc, TimeProvider.System.GetLocalNow()) : string.Empty;

	/// <summary>The most recent activity of the session (prompt, response or draft edit).</summary>
	private DateTimeOffset? LastActivity => State?.LastActivityUtc ?? State?.LastPromptUtc;

	/// <summary>The recency tier of the row (see <see cref="SessionDisplay.RecencyLevel"/>); 0 for directories and groups.</summary>
	public int RecencyLevel => IsSessionLike ? SessionDisplay.RecencyLevel(LastActivity, State?.HasDraft == true, TimeProvider.System.GetUtcNow()) : 0;

	/// <summary>Activity within the last 24 hours: the dotted border.</summary>
	public bool IsRecentlyActive => IsSessionLike && SessionDisplay.IsRecentlyActive(LastActivity, TimeProvider.System.GetUtcNow());

	/// <summary>The kind of the most severe open Focus item in lower case; empty without one. Colours the count pill.</summary>
	public string AttentionKey => IsSessionLike ? State?.Attention?.TopKind.ToString().ToLowerInvariant() ?? string.Empty : string.Empty;

	/// <summary>Re-reads the marks that age with the clock (the 60 second refresh): the recency tier and the dotted border.</summary>
	public void RefreshClock()
	{
		OnPropertyChanged(nameof(RecencyLevel));
		OnPropertyChanged(nameof(IsRecentlyActive));
		OnPropertyChanged(nameof(LastPromptText));   // an age ("5m") grows with the clock
	}

	/// <summary>Re-reads the date text (the date format setting changed).</summary>
	public void RefreshDisplay() => OnPropertyChanged(nameof(LastPromptText));

	public bool IsFavorite => State?.Favorite == true;

	/// <summary>A schedule (a delayed send, a cron job, a mailbox delivery) targets the session: the clock on its row (W8).</summary>
	public bool HasSchedules => IsSessionLike && State?.HasSchedules == true;

	public int AttentionCount => State?.Attention?.Count ?? 0;

	public bool HasAttention => AttentionCount > 0;

	/// <summary>
	/// The display name of the profile the session is pinned to (ISSUE.013 R5); without a pin (an older daemon, or a session that never ran) the profile stamped on
	/// its last user entry. Empty for directories and groups and while unknown.
	/// </summary>
	public string ProfileName => IsSessionLike ? State?.PinnedProfileName ?? State?.ProfileName ?? string.Empty : string.Empty;

	public bool HasProfile => ProfileName.Length > 0;

	/// <summary>The profile a queued switch moves the session to when its turn ends; empty when none is queued.</summary>
	public string SwitchingToProfileName => IsSessionLike ? State?.SwitchingToProfileName ?? string.Empty : string.Empty;

	public bool HasSwitching => SwitchingToProfileName.Length > 0;

	public bool ShowProfileBadge => HasProfile || HasSwitching;

	/// <summary>True when a loaded worker runs on another profile than this session (ISSUE.013 R5: warn when a supervisor and its workers differ).</summary>
	public bool HasProfileMismatch => HasProfile && Children.Any(c => c.HasProfile && !string.Equals(c.ProfileName, ProfileName, StringComparison.OrdinalIgnoreCase));

	public string ProfileToolTip
	{
		get
		{
			var tip = string.Format(Constants.Profiles.BadgeTip, ProfileName);
			if (HasSwitching)
				tip += " - " + string.Format(Constants.Profiles.BadgeSwitchingTip, SwitchingToProfileName);

			return HasProfileMismatch ? tip + " - " + Constants.Profiles.BadgeMismatchTip : tip;
		}
	}

	/// <summary>Re-evaluates the expander after the worker count or the loaded children changed.</summary>
	public void RefreshExpander()
	{
		OnPropertyChanged(nameof(CanExpand));
		OnPropertyChanged(nameof(WorkerCountText));
		OnPropertyChanged(nameof(HasWorkers));
		RefreshProfileMismatch();
	}

	/// <summary>Re-evaluates the mismatch warning of this row after a worker's profile changed or the workers loaded.</summary>
	public void RefreshProfileMismatch()
	{
		OnPropertyChanged(nameof(HasProfileMismatch));
		OnPropertyChanged(nameof(ProfileToolTip));
	}

	partial void OnStateChanged(SessionStateDto? oldValue, SessionStateDto? newValue)
	{
		OnPropertyChanged(nameof(CanExpand));

		// The supervisor's warning depends only on the profile of its workers: a running flag or an activity time changes nothing there.
		if (ProfileKey(oldValue) != ProfileKey(newValue))
			Parent?.RefreshProfileMismatch();
	}

	private static string ProfileKey(SessionStateDto? state) => state?.PinnedProfileName ?? state?.ProfileName ?? string.Empty;
}
