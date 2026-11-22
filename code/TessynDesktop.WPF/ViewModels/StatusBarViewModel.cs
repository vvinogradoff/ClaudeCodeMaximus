using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using TessynDesktop.Client;
using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Infrastructure;
using TessynDesktop.WPF.Services;

namespace TessynDesktop.WPF.ViewModels;

/// <summary>
/// The status bar's model line and the two usage meters (FR.18). The meters belong to the profile of the open session ("default" when it has none picked): the
/// daemon answers <c>usage.get</c> for it, and a <c>usage.changed</c> for that profile asks again. The model line is the selected model of the session.
/// Contains no WPF types.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class StatusBarViewModel : ObservableObject, IDisposable
{
	private readonly IUiDispatcher _ui;
	private readonly ILogger<StatusBarViewModel> _logger;
	private ITessynClient? _client;
	private SessionPaneViewModel? _session;
	private string _profileId = Constants.StatusBar.DefaultProfileId;
	private int _generation;

	[ObservableProperty]
	private string _modelText = string.Empty;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(HasNote))]
	private string _usageNote = string.Empty;

	[ObservableProperty]
	private bool _hasUsage;

	[ObservableProperty]
	private double _fiveHourPercent;

	[ObservableProperty]
	private double _sevenDayPercent;

	[ObservableProperty]
	private string _fiveHourLabel = string.Empty;

	[ObservableProperty]
	private string _sevenDayLabel = string.Empty;

	/// <summary>The top bar has a window (always for Claude; a ChatGPT plan may have none, FR.33.10).</summary>
	[ObservableProperty]
	private bool _hasFiveHour = true;

	[ObservableProperty]
	private bool _hasSevenDay = true;

	/// <summary>FR.33.10: the window ran out of credits, shown in the limits-error colour (FR.9.17).</summary>
	[ObservableProperty]
	private bool _isFiveHourExhausted;

	[ObservableProperty]
	private bool _isSevenDayExhausted;

	/// <summary>Why the window is exhausted (Codex's reason and the reset); null when it is not.</summary>
	[ObservableProperty]
	private string? _fiveHourToolTip;

	[ObservableProperty]
	private string? _sevenDayToolTip;

	public StatusBarViewModel(ConnectionViewModel connection, MainViewModel main, IUiDispatcher ui, ILogger<StatusBarViewModel> logger)
	{
		_ui = ui;
		_logger = logger;
		connection.ClientReady += client => _ui.Post(() => Attach(client));
		main.PropertyChanged += (_, e) =>
		{
			if (e.PropertyName == nameof(MainViewModel.Session))
				Observe(main.Session);
		};

		// The order of construction and of the first ClientReady is not guaranteed: pick up what is already there.
		if (connection.Client != null)
			Attach(connection.Client);

		if (main.Session != null)
			Observe(main.Session);
	}

	public bool HasNote => !string.IsNullOrEmpty(UsageNote);

	/// <summary>The profile whose usage the meters show.</summary>
	public string ProfileId => _profileId;

	/// <summary>Connects to the daemon: reads the usage now and follows its changes (also after a resync).</summary>
	public void Attach(ITessynClient client)
	{
		if (ReferenceEquals(client, _client))
			return;

		if (_client != null)
		{
			_client.NotificationReceived -= OnNotification;
			_client.Resynced -= OnResynced;
		}

		_client = client;
		client.NotificationReceived += OnNotification;
		client.Resynced += OnResynced;
		_ = RefreshAsync();
	}

	/// <summary>Follows the open session's model and profile.</summary>
	public void Observe(SessionPaneViewModel? session)
	{
		if (ReferenceEquals(session, _session))
			return;

		if (_session != null)
			_session.PropertyChanged -= OnSessionChanged;

		_session = session;
		if (session != null)
			session.PropertyChanged += OnSessionChanged;

		UpdateModel();
		UpdateProfile();
	}

	private void OnSessionChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName is nameof(SessionPaneViewModel.SelectedModel) or nameof(SessionPaneViewModel.HasSession))
			_ui.Post(UpdateModel);

		if (e.PropertyName is nameof(SessionPaneViewModel.SelectedProfile) or nameof(SessionPaneViewModel.HasSession))
			_ui.Post(UpdateProfile);
	}

	private void UpdateModel()
	{
		var model = _session?.SelectedModel;
		ModelText = _session is { HasSession: true } && model is { IsSelectable: true }
			? model.IsCodex ? StatusBarText.GptModel(model.Label) : StatusBarText.Model(model.Id, model.Provider)
			: string.Empty;
	}

	private void UpdateProfile()
	{
		var id = _session is { HasSession: true } && _session.SelectedProfile is { } profile && profile.Id.Length > 0 ? profile.Id : Constants.StatusBar.DefaultProfileId;
		if (id == _profileId)
			return;

		_profileId = id;
		OnPropertyChanged(nameof(ProfileId));
		_ = RefreshAsync();
	}

	private void OnNotification(object? sender, RemoteNotification notification)
	{
		if (notification.Method != RemoteProtocol.Events.UsageChanged)
			return;

		var changed = notification.Params["profileId"]?.GetValue<string>();
		if (changed == null || changed == _profileId)
			_ = RefreshAsync();
	}

	private void OnResynced(object? sender, ResyncedEventArgs e) => _ = RefreshAsync();

	/// <summary>Asks the daemon for the usage of the current profile and shows it; a failure keeps the last reading.</summary>
	public async Task RefreshAsync()
	{
		var client = _client;
		if (client == null)
			return;

		var generation = Interlocked.Increment(ref _generation);
		var profile = _profileId;
		try
		{
			var usage = await client.UsageGetAsync(profile);
			_ui.Post(() =>
			{
				// A newer request or another profile has taken over while this one was in flight.
				if (generation == Volatile.Read(ref _generation) && profile == _profileId)
					Show(usage);
			});
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Reading the usage of profile {Profile} failed", profile);
		}
	}

	private void Show(UsageResult usage)
	{
		switch (usage.State)
		{
			// A ChatGPT plan can lack a window (Pro has no short one, FR.33.10): that bar is hidden, the other one still shows.
			case UsageState.Ok when usage.FiveHour != null || usage.SevenDay != null:
				FiveHourPercent = Math.Clamp(usage.FiveHour?.Utilization ?? 0, 0, 100);
				SevenDayPercent = Math.Clamp(usage.SevenDay?.Utilization ?? 0, 0, 100);
				FiveHourLabel = usage.FiveHour == null ? string.Empty : StatusBarText.FiveHour(usage.FiveHour);
				SevenDayLabel = usage.SevenDay == null ? string.Empty : StatusBarText.SevenDay(usage.SevenDay);
				HasFiveHour = usage.FiveHour != null;
				HasSevenDay = usage.SevenDay != null;
				IsFiveHourExhausted = usage.FiveHour?.IsExhausted == true;
				IsSevenDayExhausted = usage.SevenDay?.IsExhausted == true;
				FiveHourToolTip = StatusBarText.ExhaustedToolTip(usage.FiveHour, usage.Exhausted);
				SevenDayToolTip = StatusBarText.ExhaustedToolTip(usage.SevenDay, usage.Exhausted);
				UsageNote = string.Empty;
				HasUsage = true;
				break;
			case UsageState.Unauthorized:
				HasUsage = false;
				UsageNote = Constants.StatusBar.UsageUnauthorized;
				break;
			case UsageState.Unavailable:
				HasUsage = false;
				UsageNote = Constants.StatusBar.UsageUnavailable;
				break;
			default:   // Pending: the daemon has no reading yet; usage.changed follows
				HasUsage = false;
				UsageNote = string.Empty;
				break;
		}
	}

	public void Dispose()
	{
		if (_client != null)
		{
			_client.NotificationReceived -= OnNotification;
			_client.Resynced -= OnResynced;
		}

		if (_session != null)
			_session.PropertyChanged -= OnSessionChanged;
	}
}
