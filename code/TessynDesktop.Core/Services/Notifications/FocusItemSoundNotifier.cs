using System;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;
using Serilog;

namespace TessynDesktop.Services;

/// <summary>
/// The daemon's trigger of the Focus item sound (FR.22.1): listens to <see cref="IAttentionService.ItemsCreated"/>, so only a new item counts, never an
/// update, a close or a turn completion. For the first new item of a burst whose session has its switch on (<c>PlayCompletionSound</c>, FR.22.2) it
/// publishes the local-stream event <c>focus.itemCreated</c> to a client that presents the sound channel and hands the cue to
/// <see cref="ISessionSoundService"/>, which plays on the host unless that client acknowledges (FR.22.6). One sound per global throttle window (FR.22.3).
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class FocusItemSoundNotifier : IDisposable
{
	private static readonly ILogger _log = Log.ForContext<FocusItemSoundNotifier>();

	private readonly IAttentionService _attention;
	private readonly ISessionRegistry _registry;
	private readonly IAppSettingsService _appSettings;
	private readonly IClientPresenceRegistry _presence;
	private readonly IRemoteEventBus _bus;
	private readonly ISessionSoundService _sounds;
	private readonly TimeProvider _time;
	private readonly object _gate = new();

	private DateTimeOffset? _lastSoundAt;
	private bool _started;

	public FocusItemSoundNotifier(
		IAttentionService attention, ISessionRegistry registry, IAppSettingsService appSettings, IClientPresenceRegistry presence,
		IRemoteEventBus bus, ISessionSoundService sounds, TimeProvider? time = null)
	{
		_attention   = attention;
		_registry    = registry;
		_appSettings = appSettings;
		_presence    = presence;
		_bus         = bus;
		_sounds      = sounds;
		_time        = time ?? TimeProvider.System;
	}

	/// <summary>Starts listening to the attention service. Safe to call twice.</summary>
	public void Start()
	{
		lock (_gate)
		{
			if (_started)
				return;

			_started = true;
			_attention.ItemsCreated += OnItemsCreated;
		}
	}

	private void OnItemsCreated(object? sender, AttentionItemsCreatedEventArgs e)
	{
		try
		{
			var node = _registry.FindModelByNodeId(e.NodeId);
			if (node is { PlayCompletionSound: false } || e.Items.Count == 0)
				return;

			lock (_gate)
			{
				var window = TimeSpan.FromSeconds(RemoteProtocol.Notifications.EffectiveSoundThrottleSeconds(_appSettings.Settings.SessionCompletionSoundThrottleSeconds));
				var now    = _time.GetUtcNow();
				if (_lastSoundAt is { } last && now - last < window)
					return;

				_lastSoundAt = now;
			}

			// The host's cue goes first so that its wait for the acknowledgement starts before the client can answer.
			_sounds.PlayFocusItemSound(e.NodeId);

			if (_presence.IsPresented(RemoteProtocol.Notifications.SoundChannel))
			{
				var first = e.Items[0];
				_bus.Publish(
					RemoteProtocol.Notifications.FocusItemCreatedEvent,
					new FocusItemCreatedPayload(e.NodeId, first.Id, AttentionFormat.KindName(first.Kind), _time.GetUtcNow()));
			}
		}
		catch (Exception ex)
		{
			_log.Warning(ex, "FocusItemSoundNotifier: could not handle new items of node {NodeId}", e.NodeId);
		}
	}

	public void Dispose()
	{
		lock (_gate)
		{
			if (!_started)
				return;

			_started = false;
			_attention.ItemsCreated -= OnItemsCreated;
		}
	}
}
