using System;
using System.Collections.ObjectModel;
using System.Reactive.Linq;
using ReactiveUI;
using TessynDesktop.Models;
using TessynDesktop.Services;

namespace TessynDesktop.ViewModels;

/// <summary>
/// The Tag row of one session (FR.24.7, FR.24.8): chips for the Linear issues, pull requests and repositories
/// the session is about. Reads tags only through <see cref="IDaemonClient"/> and refreshes on the UI thread when
/// <c>SessionTagsChanged</c> fires for its node or for all nodes, and when the session is renamed.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class SessionTagsViewModel : ViewModelBase, IDisposable
{
	private readonly string _nodeId;
	private readonly IDaemonClient _daemonClient;
	private readonly IUiDispatcher _dispatcher;
	private readonly SessionTagLauncher _launcher;
	private readonly IDisposable _renameSubscription;
	private readonly TagAreaMode _mode = new();

	/// <param name="sessionName">Emits the session's name (current value first); each later value triggers a refresh, because name-derived tags follow the name.</param>
	public SessionTagsViewModel(string nodeId, IDaemonClient daemonClient, IUiDispatcher dispatcher,
		SessionTagLauncher launcher, IObservable<string> sessionName)
	{
		_nodeId = nodeId;
		_daemonClient = daemonClient;
		_dispatcher = dispatcher;
		_launcher = launcher;

		_daemonClient.SessionTagsChanged += OnSessionTagsChanged;
		_mode.Changed += OnModeChanged;
		_renameSubscription = sessionName.Skip(1).Subscribe(_ => _dispatcher.Post(Refresh));
		Refresh();
	}

	public ObservableCollection<SessionTagChipViewModel> Chips { get; } = [];

	/// <summary>The row is hidden when the session has no tags.</summary>
	public bool HasTags => Chips.Count > 0;

	/// <summary>This session's tag area mode (FR.24.16): true = full, false = mini. Not remembered; <see cref="ResetMode"/> collapses it when the session is opened.</summary>
	public bool IsExpanded
	{
		get => _mode.IsExpanded;
		set => _mode.Set(value);
	}

	/// <summary>A session was opened: it starts collapsed (FR.24.16), also when it was expanded the last time it was shown.</summary>
	public void ResetMode() => _mode.Reset();

	/// <summary>Re-reads the mode for the view.</summary>
	public void RefreshMode() => this.RaisePropertyChanged(nameof(IsExpanded));

	public void Dispose()
	{
		_daemonClient.SessionTagsChanged -= OnSessionTagsChanged;
		_mode.Changed -= OnModeChanged;
		_renameSubscription.Dispose();
	}

	private void OnModeChanged() => _dispatcher.Post(RefreshMode);

	private void OnSessionTagsChanged(object? sender, SessionTagsChangedEventArgs e)
	{
		// NodeId == null: every session may be affected (a Linear team was learned).
		if (e.NodeId is not null && !string.Equals(e.NodeId, _nodeId, StringComparison.Ordinal))
			return;

		_dispatcher.Post(Refresh);
	}

	/// <summary>Rebuilds the chips from the daemon (already in FR.24.7 order). Must run on the UI thread.</summary>
	private void Refresh()
	{
		var tags = _daemonClient.GetSessionTags(_nodeId);

		Chips.Clear();
		foreach (var tag in tags)
			Chips.Add(new SessionTagChipViewModel(tag, _launcher));

		this.RaisePropertyChanged(nameof(HasTags));
	}
}
