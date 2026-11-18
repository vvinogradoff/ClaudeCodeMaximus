using Microsoft.Extensions.Logging;
using TessynDesktop.Client;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.WPF.Services;

/// <summary>
/// The Focus item sound of the WPF client (FR.22, W1b). The client registers the <c>sound</c> channel with the daemon, so the desktop host stays
/// silent while it is connected; this service plays the sound itself when the daemon announces a new Focus item (<c>focus.itemCreated</c>) and tells
/// the daemon with <c>notification.ack { nodeId }</c> (the host plays it after about 3 s when no ack comes). A turn completion, an item update and a
/// resolve announce nothing, so they never play. The rules: only when the owning session's "play a sound when a new Focus item appears" option is on,
/// at most one sound per global throttle window (a burst is one sound), never for a replayed event, with the file the Settings window chose.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class FocusItemSoundService : IDisposable
{
	private readonly ITessynClient _client;
	private readonly ISoundPlayer _player;
	private readonly TimeProvider _clock;
	private readonly ILogger<FocusItemSoundService> _logger;
	private readonly object _lock = new();

	private DateTimeOffset? _lastPlayed;
	private NotificationsSettingsDto? _settings;
	private DateTimeOffset _settingsReadAt;
	private bool _attached;

	public FocusItemSoundService(ITessynClient client, ISoundPlayer player, TimeProvider clock, ILogger<FocusItemSoundService> logger)
	{
		_client = client;
		_player = player;
		_clock = clock;
		_logger = logger;
	}

	/// <summary>Completes when no event is being handled (tests).</summary>
	public Task WhenIdleAsync()
	{
		lock (_lock)
			return _pending;
	}

	private Task _pending = Task.CompletedTask;

	/// <summary>Starts listening to the client's notifications.</summary>
	public void Attach()
	{
		if (_attached)
			return;

		_attached = true;
		_client.NotificationReceived += OnNotification;
	}

	private void OnNotification(object? sender, RemoteNotification notification)
	{
		if (!notification.TryGetFocusItemCreated(out var created))
			return;

		lock (_lock)
			_pending = Task.WhenAll(_pending, HandleAsync(created));
	}

	private async Task HandleAsync(FocusItemCreatedPayload created)
	{
		try
		{
			// The event log replays after a reconnect: an old cue is not played (and not acknowledged; the host decided long ago).
			if (created.IsReplay(_clock.GetUtcNow()))
				return;

			if (!await SessionWantsSoundAsync(created.NodeId).ConfigureAwait(false))
				return;

			var settings = await ReadSettingsAsync().ConfigureAwait(false);
			if (settings == null || string.IsNullOrWhiteSpace(settings.SessionCompletionSoundFile))
				return;   // nothing to play: no ack, so the host plays its own after the wait

			if (!Throttled(RemoteProtocol.Notifications.EffectiveSoundThrottleSeconds(settings.SessionCompletionSoundThrottleSeconds)))
				_player.Play(settings.SessionCompletionSoundFile);

			// A throttled sound is acknowledged too: staying silent was this client's decision, and the host would not play it either.
			await _client.NotificationSoundAckAsync(created.NodeId).ConfigureAwait(false);
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Handling the Focus item sound of {NodeId} failed", created.NodeId);
		}
	}

	/// <summary>The session's own switch (FR.22.2); an older daemon without the call, or a failure, means "yes".</summary>
	private async Task<bool> SessionWantsSoundAsync(string nodeId)
	{
		try
		{
			// No field set: the daemon changes nothing and answers the current options.
			var result = await _client.SessionSetOptionsAsync(nodeId).ConfigureAwait(false);
			return result.Options.PlayCompletionSound;
		}
		catch (Exception ex)
		{
			_logger.LogInformation("Could not read the Focus item sound option of {NodeId} ({Message}); playing", nodeId, ex.Message);
			return true;
		}
	}

	private async Task<NotificationsSettingsDto?> ReadSettingsAsync()
	{
		var now = _clock.GetUtcNow();
		lock (_lock)
		{
			if (_settings != null && now - _settingsReadAt < Constants.SoundRules.SettingsCacheAge)
				return _settings;
		}

		try
		{
			var result = await _client.SettingsGetAsync<NotificationsSettingsDto>(RemoteProtocol.SettingsApi.NotificationsSection).ConfigureAwait(false);
			lock (_lock)
			{
				_settings = result.Values;
				_settingsReadAt = now;
			}

			return result.Values;
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Reading the notification settings failed");
			lock (_lock)
				return _settings;   // an old copy is better than silence
		}
	}

	/// <summary>True when a sound played inside the global window (FR.22.3); otherwise starts the window now.</summary>
	private bool Throttled(int throttleSeconds)
	{
		var now = _clock.GetUtcNow();
		var window = TimeSpan.FromSeconds(Math.Max(0, throttleSeconds));
		lock (_lock)
		{
			if (_lastPlayed is { } last && now - last < window)
				return true;

			_lastPlayed = now;
			return false;
		}
	}

	public void Dispose()
	{
		if (_attached)
			_client.NotificationReceived -= OnNotification;

		_attached = false;
	}
}
