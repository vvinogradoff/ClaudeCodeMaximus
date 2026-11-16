using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TessynDesktop.Client;
using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Infrastructure;

namespace TessynDesktop.WPF.ViewModels;

/// <summary>
/// The schedules of the open session (W8, FR.14): the list behind the indicator in the session header, with Cancel for a user schedule and the mailbox deliveries
/// of async workers read-only. Loaded with <c>schedules.list</c> when the session changes and again on <c>schedule.changed</c> (for this session or an unspecified one).
/// Not offered (empty, no indicator) when the daemon does not announce the <c>schedules</c> capability, e.g. an older daemon.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class ScheduleViewModel : ObservableObject, IDisposable
{
	private readonly ITessynClient _client;
	private readonly IUiDispatcher _ui;
	private readonly ILogger _logger;
	private readonly TimeZoneInfo _zone;
	private string? _nodeId;
	private int _loadToken;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(HasSchedules), nameof(Count))]
	private int _total;

	/// <summary>The popup with the list is open.</summary>
	[ObservableProperty]
	private bool _isOpen;

	[ObservableProperty]
	private string? _status;

	public ScheduleViewModel(ITessynClient client, IUiDispatcher ui, ILogger logger, TimeZoneInfo? zone = null)
	{
		_client = client;
		_ui = ui;
		_logger = logger;
		_zone = zone ?? TimeZoneInfo.Local;
		_client.NotificationReceived += OnNotification;
	}

	/// <summary>The schedules a user made (delayed sends, cron).</summary>
	public ObservableCollection<ScheduleItemViewModel> Own { get; } = [];

	/// <summary>Mailbox deliveries of async workers; read-only.</summary>
	public ObservableCollection<ScheduleItemViewModel> Mailbox { get; } = [];

	/// <summary>True when the daemon announces <c>schedules</c>.</summary>
	public bool IsSupported => _client.ServerInfo?.Capabilities.Contains(RemoteProtocol.Schedules.Capability) == true;

	public bool HasSchedules => Total > 0;

	public int Count => Total;

	public bool HasMailbox => Mailbox.Count > 0;

	public bool HasOwn => Own.Count > 0;

	/// <summary>Points the list at another session (null = none) and loads it.</summary>
	public void SetSession(string? nodeId)
	{
		if (nodeId == _nodeId)
			return;

		_nodeId = nodeId;
		IsOpen = false;
		Status = null;
		Interlocked.Increment(ref _loadToken);
		Apply([]);
		if (nodeId != null)
			_ = RefreshAsync();
	}

	/// <summary>Reloads the list of the current session; a failure is shown in <see cref="Status"/>.</summary>
	public async Task RefreshAsync()
	{
		var nodeId = _nodeId;
		if (nodeId == null || !IsSupported)
			return;

		var token = Interlocked.Increment(ref _loadToken);
		try
		{
			var result = await _client.SchedulesListAsync(nodeId);
			_ui.Post(() =>
			{
				if (token == _loadToken && nodeId == _nodeId)
				{
					Status = null;
					Apply(result.Schedules);
				}
			});
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			_logger.LogWarning(ex, "schedules.list failed for {NodeId}", nodeId);
			_ui.Post(() =>
			{
				if (token == _loadToken && nodeId == _nodeId)
					Status = Constants.Schedules.LoadFailed + ex.Message;
			});
		}
	}

	[RelayCommand]
	private async Task CancelAsync(ScheduleItemViewModel? item)
	{
		if (item is not { CanCancel: true })
			return;

		try
		{
			await _client.SchedulesRemoveAsync(item.Id);
			await RefreshAsync();
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			_logger.LogWarning(ex, "schedules.remove failed for {ScheduleId}", item.Id);
			Status = Constants.Schedules.CancelFailed + ex.Message;
		}
	}

	[RelayCommand]
	private void Toggle() => IsOpen = !IsOpen;

	private void Apply(IReadOnlyList<ScheduleDto> schedules)
	{
		Own.Clear();
		Mailbox.Clear();
		foreach (var schedule in schedules.OrderBy(s => s.FireAtUtc ?? DateTimeOffset.MaxValue))
			(schedule.IsMailbox ? Mailbox : Own).Add(new ScheduleItemViewModel(schedule, _zone, CancelCommand));

		Total = Own.Count + Mailbox.Count;
		OnPropertyChanged(nameof(HasMailbox));
		OnPropertyChanged(nameof(HasOwn));
		if (Total == 0)
			IsOpen = false;
	}

	private void OnNotification(object? sender, RemoteNotification notification)
	{
		if (!notification.TryGetScheduleChanged(out var payload))
			return;

		_ui.Post(() =>
		{
			if (payload.NodeId == null || payload.NodeId == _nodeId)
				_ = RefreshAsync();
		});
	}

	public void Dispose() => _client.NotificationReceived -= OnNotification;
}
