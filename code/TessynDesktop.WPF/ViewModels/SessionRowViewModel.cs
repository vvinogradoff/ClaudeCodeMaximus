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
	[NotifyPropertyChangedFor(nameof(Label), nameof(OpenAutomationName), nameof(FavoriteAutomationName), nameof(HideAutomationName))]
	private string _name;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(IsRunning), nameof(IsFavorite), nameof(AttentionCount), nameof(HasAttention), nameof(HasDraft), nameof(ProfileName), nameof(HasSwitching), nameof(SwitchingToProfileName),
		nameof(LastPromptText), nameof(RecencyLevel), nameof(IsRecentlyActive), nameof(IsResumable), nameof(AttentionKey), nameof(ShowStar), nameof(ShowHide), nameof(ShowStarToggle))]
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

	/// <summary>The profile the session is pinned to, else the one stamped on its last prompt; empty while unknown (as the tree row shows it).</summary>
	public string ProfileName => State.PinnedProfileName ?? State.ProfileName ?? string.Empty;

	/// <summary>The profile a queued switch moves the session to when its turn ends; empty when none is queued.</summary>
	public string SwitchingToProfileName => State.SwitchingToProfileName ?? string.Empty;

	public bool HasSwitching => SwitchingToProfileName.Length > 0;

	/// <summary>The session's name; the shared row face binds the same name for a tree node and a list row.</summary>
	public string Label => Name;

	/// <summary>Date of the last prompt in the format of the Appearance setting (<c>yyyy-MM-dd HH:mm</c> by default, as the Avalonia client); empty without a prompt.</summary>
	public string LastPromptText => SessionDisplay.DateText(State.LastPromptUtc, _clock.GetLocalNow());

	/// <summary>The most recent activity (prompt, response or draft edit); the last prompt when the daemon did not say.</summary>
	private DateTimeOffset? LastActivity => State.LastActivityUtc ?? State.LastPromptUtc;

	/// <summary>FR.9.5, as the Avalonia client: 4 = activity within 15 minutes, 3 = within an hour, 2 = within 4 hours, 1 = within 12 hours or an unsent draft, 0 = none.</summary>
	public int RecencyLevel => SessionDisplay.RecencyLevel(LastActivity, HasDraft, _clock.GetUtcNow());

	/// <summary>Activity within the last 24 hours: the dotted border.</summary>
	public bool IsRecentlyActive => SessionDisplay.IsRecentlyActive(LastActivity, _clock.GetUtcNow());

	/// <summary>Claude Code still has the session for <c>--resume</c>: the lower blue square.</summary>
	public bool IsResumable => State.Resumable == true;

	/// <summary>The kind of the most severe open Focus item in lower case (<c>critical</c>, <c>ask</c>, ...); empty without one. Colours the count pill.</summary>
	public string AttentionKey => State.Attention?.TopKind.ToString().ToLowerInvariant() ?? string.Empty;

	/// <summary>The one icon slot (FR.9.7): the filled star of a favourite, else Hide, else (tabs) a star to set.</summary>
	public bool ShowStar => IsFavorite;

	public bool ShowHide => !IsFavorite && CanHide;

	public bool ShowStarToggle => !IsFavorite && !CanHide && CanToggleFavorite;

	/// <summary>Re-reads the time-dependent marks (the 60 second recency refresh).</summary>
	public void RefreshClock()
	{
		OnPropertyChanged(nameof(RecencyLevel));
		OnPropertyChanged(nameof(IsRecentlyActive));
	}

	/// <summary>Re-reads the date text (the date format setting changed).</summary>
	public void RefreshDisplay() => OnPropertyChanged(nameof(LastPromptText));

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
