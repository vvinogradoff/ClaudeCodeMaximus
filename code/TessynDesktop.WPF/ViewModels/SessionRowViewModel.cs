using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.WPF.ViewModels;

/// <summary>
/// One session row of the Recent tab, the Recent dropdown or the Tags tab (FR.1.16, FR.9.7): status, name, draft mark, profile, last prompt
/// time, recency, attention and schedule marks, and the one icon slot (star when a favourite, else Hide, else a star to set).
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class SessionRowViewModel : ObservableObject
{
	private readonly TimeProvider _clock;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(OpenAutomationName), nameof(FavoriteAutomationName), nameof(HideAutomationName))]
	private string _name;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(IsRunning), nameof(IsFavorite), nameof(AttentionCount), nameof(HasAttention), nameof(HasDraft), nameof(ProfileName),
		nameof(LastPromptText), nameof(RecencyLevel), nameof(ShowStar), nameof(ShowHide), nameof(ShowStarToggle))]
	private SessionStateDto _state;

	[ObservableProperty]
	private bool _pinned;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(ShowHide), nameof(ShowStarToggle))]
	private bool _canHide;

	[ObservableProperty]
	private bool _hasSchedules;

	public SessionRowViewModel(string id, string name, string directoryLabel, SessionStateDto state, bool hasSchedules = false, bool pinned = false, bool canHide = false,
		bool showDirectory = true, bool canToggleFavorite = false, TimeProvider? clock = null)
	{
		Id = id;
		_name = name;
		DirectoryLabel = directoryLabel;
		_state = state;
		_hasSchedules = hasSchedules;
		_pinned = pinned;
		_canHide = canHide;
		ShowDirectory = showDirectory;
		CanToggleFavorite = canToggleFavorite;
		_clock = clock ?? TimeProvider.System;
	}

	public static SessionRowViewModel From(RecentRowDto row) => new(row.Id, row.Name, row.DirectoryLabel, row.State, row.HasSchedules, row.Pinned, row.CanHide);

	public static SessionRowViewModel From(SessionRowDto row) => new(row.Id, row.Name, string.Empty, row.State, showDirectory: false, canToggleFavorite: true);

	public string Id { get; }

	/// <summary>The label of the session's directory (the sub-line of a flat list).</summary>
	public string DirectoryLabel { get; }

	/// <summary>Grouped lists show the directory in the group header, flat ones under each name.</summary>
	public bool ShowDirectory { get; }

	/// <summary>The row offers a star to set the favourite flag (the tabs do; the dropdown only shows and clears it).</summary>
	public bool CanToggleFavorite { get; }

	public bool IsRunning => State.Running;

	public bool IsFavorite => State.Favorite;

	public int AttentionCount => State.Attention?.Count ?? 0;

	public bool HasAttention => AttentionCount > 0;

	public bool HasDraft => State.HasDraft;

	public string ProfileName => State.ProfileName ?? string.Empty;

	/// <summary>Time of the last prompt: the clock time today, else the date; empty without a prompt.</summary>
	public string LastPromptText
	{
		get
		{
			if (State.LastPromptUtc is not { } at)
				return string.Empty;

			var local = at.ToLocalTime();
			return local.Date == _clock.GetLocalNow().Date ? local.ToString("HH:mm") : local.ToString("MMM d");
		}
	}

	/// <summary>FR.9.5: 3 = last prompt within 15 minutes, 2 = within 30, 1 = within an hour, 0 = none.</summary>
	public int RecencyLevel
	{
		get
		{
			if (State.LastPromptUtc is not { } at)
				return 0;

			var age = _clock.GetUtcNow() - at;
			return age <= TimeSpan.FromMinutes(Constants.SessionLists.RecencyLightMinutes) ? 3
				: age <= TimeSpan.FromMinutes(Constants.SessionLists.RecencyMediumMinutes) ? 2
				: age <= TimeSpan.FromMinutes(Constants.SessionLists.RecencyDarkMinutes) ? 1 : 0;
		}
	}

	/// <summary>The one icon slot (FR.9.7): the filled star of a favourite, else Hide, else (tabs) a star to set.</summary>
	public bool ShowStar => IsFavorite;

	public bool ShowHide => !IsFavorite && CanHide;

	public bool ShowStarToggle => !IsFavorite && !CanHide && CanToggleFavorite;

	/// <summary>Re-reads the time-dependent marks (the 60 second recency refresh).</summary>
	public void RefreshClock() => OnPropertyChanged(nameof(RecencyLevel));

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
