using System;
using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Media;
using TessynDesktop.Models;
using TessynDesktop.Services;
using ReactiveUI;

namespace TessynDesktop.ViewModels;

/// <remarks>Created by Claude</remarks>
public sealed class SessionNodeViewModel : ViewModelBase
{
	private string _name;
	private bool _isRunning;
	private bool _isResumable;
	private bool _isExternallyActive;
	private bool _isVisible = true;
	private bool _isBeingMoved;
	private bool _hasDraftText;
	private bool _isExpanded;
	private string? _lastPromptTime;
	private string? _profileName;
	private DateTimeOffset? _lastPromptTimestamp;
	private DateTimeOffset? _lastResponseTimestamp;
	private DateTimeOffset? _lastDraftEditTimestamp;
	private DateTimeOffset? _recentIgnoredAt;
	private bool _isFavorite;
	private bool _hasSchedules;
	private string _directoryLabel = string.Empty;
	private IBrush? _recencyBrush;
	private bool _isRecentlyActive;

	public SessionNodeModel Model { get; }

	public string Name
	{
		get => _name;
		set
		{
			this.RaiseAndSetIfChanged(ref _name, value);
			Model.Name = value;
		}
	}

	public string FileName => Model.FileName;

	/// <summary>Stable daemon-side session identifier. Null until mapped or first run.system event.</summary>
	public string? ExternalId => Model.ExternalId;

	/// <summary>Best available identity key: ExternalId if set, otherwise FileName.</summary>
	public string SessionKey => Model.SessionKey;

	/// <summary>True while a claude process is actively running for this session.</summary>
	public bool IsRunning
	{
		get => _isRunning;
		set => this.RaiseAndSetIfChanged(ref _isRunning, value);
	}

	/// <summary>True when Claude Code still has this session available for --resume.</summary>
	public bool IsResumable
	{
		get => _isResumable;
		set => this.RaiseAndSetIfChanged(ref _isResumable, value);
	}

	/// <summary>True when this session is being actively used in an external terminal (Claude is thinking/responding).</summary>
	public bool IsExternallyActive
	{
		get => _isExternallyActive;
		set => this.RaiseAndSetIfChanged(ref _isExternallyActive, value);
	}

	/// <summary>True when the session's input text area contains text (a draft).</summary>
	public bool HasDraftText
	{
		get => _hasDraftText;
		set
		{
			this.RaiseAndSetIfChanged(ref _hasDraftText, value);
			RefreshRecencyBrush();
		}
	}

	/// <summary>Controls visibility during search filtering.</summary>
	public bool IsVisible
	{
		get => _isVisible;
		set => this.RaiseAndSetIfChanged(ref _isVisible, value);
	}

	/// <summary>True while this session is being moved (F6 / drag). Drives semi-transparent visual.</summary>
	public bool IsBeingMoved
	{
		get => _isBeingMoved;
		set
		{
			this.RaiseAndSetIfChanged(ref _isBeingMoved, value);
			this.RaisePropertyChanged(nameof(MoveOpacity));
			this.RaisePropertyChanged(nameof(MoveForeground));
		}
	}

	/// <summary>Opacity when being moved (semi-transparent) vs normal.</summary>
	public double MoveOpacity => _isBeingMoved ? 0.4 : 1.0;

	/// <summary>Text brush when being moved (grey) vs default.</summary>
	public IBrush MoveForeground => _isBeingMoved
		? Brushes.Gray
		: (Application.Current!.ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark
			? Brushes.White : Brushes.Black);

	/// <summary>Formatted date/time of the last user prompt in this session.</summary>
	public string? LastPromptTime
	{
		get => _lastPromptTime;
		set => this.RaiseAndSetIfChanged(ref _lastPromptTime, value);
	}

	/// <summary>Display name of the profile used for the last user prompt, or null if unknown/default.</summary>
	public string? ProfileName
	{
		get => _profileName;
		set => this.RaiseAndSetIfChanged(ref _profileName, value);
	}

	/// <summary>Actual timestamp of the last user prompt, used for recency calculations.</summary>
	public DateTimeOffset? LastPromptTimestamp
	{
		get => _lastPromptTimestamp;
		set
		{
			this.RaiseAndSetIfChanged(ref _lastPromptTimestamp, value);
			NotifyActivityChanged();
			RefreshRecencyBrush();
		}
	}

	/// <summary>Actual timestamp of the last assistant output received, used for recency calculations.</summary>
	public DateTimeOffset? LastResponseTimestamp
	{
		get => _lastResponseTimestamp;
		set
		{
			this.RaiseAndSetIfChanged(ref _lastResponseTimestamp, value);
			NotifyActivityChanged();
			RefreshRecencyBrush();
		}
	}

	/// <summary>Timestamp of the last unsent draft edit, or null while there is no pending draft.</summary>
	public DateTimeOffset? LastDraftEditTimestamp
	{
		get => _lastDraftEditTimestamp;
		set
		{
			this.RaiseAndSetIfChanged(ref _lastDraftEditTimestamp, value);
			NotifyActivityChanged();
			RefreshRecencyBrush();
		}
	}

	/// <summary>Most recent of the last prompt sent, last response received, and last unsent draft edit.</summary>
	public DateTimeOffset? LastActivityTimestamp
	{
		get
		{
			DateTimeOffset? result = _lastPromptTimestamp;
			if (_lastResponseTimestamp.HasValue && (!result.HasValue || _lastResponseTimestamp > result))
				result = _lastResponseTimestamp;
			if (_lastDraftEditTimestamp.HasValue && (!result.HasValue || _lastDraftEditTimestamp > result))
				result = _lastDraftEditTimestamp;
			return result;
		}
	}

	private void NotifyActivityChanged()
	{
		this.RaisePropertyChanged(nameof(LastActivityTimestamp));
		this.RaisePropertyChanged(nameof(IsHiddenFromRecent));
	}

	/// <summary>
	/// Timestamp when the user hid this session from the recent-sessions dropdown, or null if never hidden
	/// (or if hidden but activity has since occurred, in which case it's cleared back to null — see setter).
	/// </summary>
	public DateTimeOffset? RecentIgnoredAt
	{
		get => _recentIgnoredAt;
		set
		{
			this.RaiseAndSetIfChanged(ref _recentIgnoredAt, value);
			Model.RecentIgnoredAt = value;
			this.RaisePropertyChanged(nameof(IsHiddenFromRecent));
		}
	}

	/// <summary>
	/// True when the session was hidden from the recent dropdown and no activity (prompt, response, or
	/// draft edit) has occurred since. Any newer activity makes this false again automatically.
	/// </summary>
	public bool IsHiddenFromRecent =>
		_recentIgnoredAt.HasValue && (!LastActivityTimestamp.HasValue || _recentIgnoredAt.Value >= LastActivityTimestamp.Value);

	/// <summary>
	/// True when the user has marked this session as a favorite (FR.9.7). Favorited sessions always
	/// remain in the top-bar recent-sessions dropdown regardless of recency or <see cref="IsHiddenFromRecent"/>.
	/// </summary>
	public bool IsFavorite
	{
		get => _isFavorite;
		set
		{
			this.RaiseAndSetIfChanged(ref _isFavorite, value);
			Model.IsFavorite = value;
		}
	}

	/// <summary>True when this session has one or more active schedules (FR.14). Refreshed by the recent-dropdown rebuild.</summary>
	public bool HasSchedules
	{
		get => _hasSchedules;
		set => this.RaiseAndSetIfChanged(ref _hasSchedules, value);
	}

	/// <summary>Directory label for this session, shown in the recent-dropdown row. Refreshed by the recent-dropdown rebuild.</summary>
	public string DirectoryLabel
	{
		get => _directoryLabel;
		set => this.RaiseAndSetIfChanged(ref _directoryLabel, value);
	}

	/// <summary>True when the last activity was within the last 24 hours (drives dotted green border).</summary>
	public bool IsRecentlyActive
	{
		get => _isRecentlyActive;
		private set => this.RaiseAndSetIfChanged(ref _isRecentlyActive, value);
	}

	/// <summary>Background brush for the session node based on last prompt recency.</summary>
	public IBrush? RecencyBrush
	{
		get => _recencyBrush;
		private set => this.RaiseAndSetIfChanged(ref _recencyBrush, value);
	}

	private static readonly SolidColorBrush GreenBrush     = new(Color.Parse("Green"));
	private static readonly SolidColorBrush LightGreenBrush = new(Color.Parse("LightGreen"));
	private static readonly SolidColorBrush HoneyDewBrush  = new(Color.Parse("HoneyDew"));
	private static readonly SolidColorBrush MintCreamBrush = new(Color.Parse("MintCream"));

	/// <summary>Recalculates the recency brush and 24-hour active flag based on how long ago the last activity (prompt sent, response received, or draft edit) was.</summary>
	public void RefreshRecencyBrush()
	{
		var lastActivity = LastActivityTimestamp;
		var elapsed = lastActivity.HasValue
			? DateTimeOffset.UtcNow - lastActivity.Value
			: TimeSpan.MaxValue;

		IsRecentlyActive = lastActivity.HasValue && elapsed.TotalHours <= 24;

		if (!lastActivity.HasValue)
		{
			RecencyBrush = _hasDraftText ? MintCreamBrush : null;
			return;
		}

		RecencyBrush = elapsed.TotalMinutes switch
		{
			<= 15  => GreenBrush,
			<= 60  => LightGreenBrush,
			<= 240 => HoneyDewBrush,
			<= 720 => MintCreamBrush,
			_      => _hasDraftText ? MintCreamBrush : null,
		};
	}

	/// <summary>Worker sessions spawned by this session via orchestration tools.</summary>
	public ObservableCollection<SessionNodeViewModel> SubSessions { get; } = [];

	/// <summary>True when this session was created by an orchestration tool (has a supervisor).</summary>
	public bool IsSubSession => !string.IsNullOrEmpty(Model.SupervisorNodeId);

	/// <summary>Controls TreeView expand/collapse for showing sub-sessions.</summary>
	public bool IsExpanded
	{
		get => _isExpanded;
		set => this.RaiseAndSetIfChanged(ref _isExpanded, value);
	}

	public SessionNodeViewModel(SessionNodeModel model)
	{
		Model = model;
		_name = model.Name;
		_recentIgnoredAt = model.RecentIgnoredAt;
		_isFavorite = model.IsFavorite;
	}
}
