using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.WPF.ViewModels;

/// <summary>One session row of the Recent tab, the Recent dropdown or the Tags tab: name, where it lives, running state and favourite.</summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class SessionRowViewModel : ObservableObject
{
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(OpenAutomationName), nameof(FavoriteAutomationName), nameof(HideAutomationName))]
	private string _name;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(IsRunning), nameof(IsFavorite), nameof(AttentionCount), nameof(HasAttention))]
	private SessionStateDto _state;

	[ObservableProperty]
	private bool _pinned;

	[ObservableProperty]
	private bool _canHide;

	public SessionRowViewModel(string id, string name, string directoryLabel, SessionStateDto state, bool hasSchedules = false, bool pinned = false, bool canHide = false)
	{
		Id = id;
		_name = name;
		DirectoryLabel = directoryLabel;
		_state = state;
		HasSchedules = hasSchedules;
		_pinned = pinned;
		_canHide = canHide;
	}

	public static SessionRowViewModel From(RecentRowDto row) => new(row.Id, row.Name, row.DirectoryLabel, row.State, row.HasSchedules, row.Pinned, row.CanHide);

	public static SessionRowViewModel From(SessionRowDto row, string directoryLabel) => new(row.Id, row.Name, directoryLabel, row.State);

	public string Id { get; }

	/// <summary>The label of the session's directory; shown under the name.</summary>
	public string DirectoryLabel { get; }

	public bool HasSchedules { get; }

	public bool IsRunning => State.Running;

	public bool IsFavorite => State.Favorite;

	public int AttentionCount => State.Attention?.Count ?? 0;

	public bool HasAttention => AttentionCount > 0;

	/// <summary>Opens the session; the list that owns the row sets it (parameter: the row).</summary>
	public ICommand? OpenCommand { get; set; }

	/// <summary>Flips the favourite flag (parameter: the row).</summary>
	public ICommand? ToggleFavoriteCommand { get; set; }

	/// <summary>Hides the row from the Recent list (parameter: the row); null in lists that cannot hide.</summary>
	public ICommand? HideCommand { get; set; }

	/// <summary>Accessible names of the row's buttons (the name is the session's, so each row's buttons are told apart).</summary>
	public string OpenAutomationName => Constants.SessionLists.OpenButtonPrefix + Name;

	public string FavoriteAutomationName => Constants.SessionLists.FavoriteButtonName + " " + Name;

	public string HideAutomationName => Constants.SessionLists.HideButtonPrefix + Name;
}
