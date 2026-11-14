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
	[NotifyPropertyChangedFor(nameof(Glyph))]
	private bool _isExpanded;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(IsRunning), nameof(IsFavorite), nameof(AttentionCount), nameof(HasAttention))]
	private SessionStateDto? _state;

	[ObservableProperty]
	private bool _isLoadingWorkers;

	[ObservableProperty]
	private string? _matchSummary;

	public TreeNodeViewModel(string id, TreeNodeKind kind, string label, TreeNodeViewModel? parent, int workerCount = 0)
	{
		Id = id;
		Kind = kind;
		_label = label;
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

	public string Glyph => !CanExpand ? string.Empty : IsExpanded ? "v" : ">";

	public bool IsRunning => State?.Running == true;

	public bool IsFavorite => State?.Favorite == true;

	public int AttentionCount => State?.Attention?.Count ?? 0;

	public bool HasAttention => AttentionCount > 0;

	/// <summary>Re-evaluates the expander after the worker count or the loaded children changed.</summary>
	public void RefreshExpander()
	{
		OnPropertyChanged(nameof(CanExpand));
		OnPropertyChanged(nameof(Glyph));
	}

	partial void OnStateChanged(SessionStateDto? value) => OnPropertyChanged(nameof(CanExpand));
}
