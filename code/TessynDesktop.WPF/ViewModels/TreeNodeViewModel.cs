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
	[NotifyPropertyChangedFor(nameof(IsRunning), nameof(IsFavorite), nameof(AttentionCount), nameof(HasAttention), nameof(ProfileName), nameof(HasProfile), nameof(HasProfileMismatch), nameof(ProfileToolTip))]
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

	/// <summary>Whether the row shows an expander.</summary>
	public bool CanExpand => IsSessionLike ? WorkerCount > 0 : Children.Count > 0;

	/// <summary>True once <c>tree.nodeInfo</c> answered for this node (so a tooltip needs no second call).</summary>
	public bool InfoLoaded { get; set; }

	public bool HasGitOrigin => !string.IsNullOrEmpty(GitOrigin);

	/// <summary>The short form of the git origin shown beside a directory (<c>owner/repo</c>); empty without one.</summary>
	public string GitOriginLabel => GitOrigin == null ? string.Empty : GitOriginText.Short(GitOrigin);

	public bool IsRunning => State?.Running == true;

	public bool IsFavorite => State?.Favorite == true;

	public int AttentionCount => State?.Attention?.Count ?? 0;

	public bool HasAttention => AttentionCount > 0;

	/// <summary>The display name of the profile the session last ran on (ISSUE.013 R5); empty for directories and groups and while unknown.</summary>
	public string ProfileName => IsSessionLike ? State?.ProfileName ?? string.Empty : string.Empty;

	public bool HasProfile => ProfileName.Length > 0;

	/// <summary>True when a loaded worker runs on another profile than this session (ISSUE.013 R5: warn when a supervisor and its workers differ).</summary>
	public bool HasProfileMismatch => HasProfile && Children.Any(c => c.HasProfile && !string.Equals(c.ProfileName, ProfileName, StringComparison.OrdinalIgnoreCase));

	public string ProfileToolTip => HasProfileMismatch
		? string.Format(Constants.Profiles.BadgeTip, ProfileName) + " - " + Constants.Profiles.BadgeMismatchTip
		: string.Format(Constants.Profiles.BadgeTip, ProfileName);

	/// <summary>Re-evaluates the expander after the worker count or the loaded children changed.</summary>
	public void RefreshExpander()
	{
		OnPropertyChanged(nameof(CanExpand));
		RefreshProfileMismatch();
	}

	/// <summary>Re-evaluates the mismatch warning of this row after a worker's profile changed or the workers loaded.</summary>
	public void RefreshProfileMismatch()
	{
		OnPropertyChanged(nameof(HasProfileMismatch));
		OnPropertyChanged(nameof(ProfileToolTip));
	}

	partial void OnStateChanged(SessionStateDto? value)
	{
		OnPropertyChanged(nameof(CanExpand));
		Parent?.RefreshProfileMismatch();
	}
}
