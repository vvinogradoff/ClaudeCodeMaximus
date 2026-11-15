using Microsoft.Extensions.Logging;
using TessynDesktop.Client;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.WPF.Services;

/// <summary>
/// The completion sound of the WPF client (FR.22, W1b). The client registers the <c>sound</c> channel with the daemon, so the desktop host stays
/// silent while it is connected; this service plays the sound itself when a turn completes and tells the daemon with <c>notification.ack { nodeId }</c>
/// (the host plays it after about 3 s when no ack comes). The rules are the host's: never after a stop, never when the turn never ran, only when the
/// session's "play completion sound" option is on, at most once per session per throttle window, with the file the Settings window chose.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class CompletionSoundService : IDisposable
{
	private readonly ITessynClient _client;
	private readonly ISoundPlayer _player;
	private readonly TimeProvider _clock;
	private readonly ILogger<CompletionSoundService> _logger;
	private readonly Dictionary<string, DateTimeOffset> _lastPlayed = new();
	private readonly object _lock = new();

	private NotificationsSettingsDto? _settings;
	private DateTimeOffset _settingsReadAt;
	private bool _attached;

	public CompletionSoundService(ITessynClient client, ISoundPlayer player, TimeProvider clock, ILogger<CompletionSoundService> logger)
	{
		_client = client;
		_player = player;
		_clock = clock;
		_logger = logger;
	}

	/// <summary>Completes when no completion is being handled (tests).</summary>
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
		if (!notification.TryGetTurnCompleted(out var completed))
			return;

		lock (_lock)
			_pending = Task.WhenAll(_pending, HandleAsync(completed));
	}

	private async Task HandleAsync(TurnCompletedPayload completed)
	{
		try
		{
			if (completed.Cancelled || completed.NeverRan == true)
				return;

			if (!await SessionWantsSoundAsync(completed.NodeId).ConfigureAwait(false))
				return;

			var settings = await ReadSettingsAsync().ConfigureAwait(false);
			if (settings == null || string.IsNullOrWhiteSpace(settings.SessionCompletionSoundFile))
				return;   // nothing to play: no ack, so the host plays its own after the wait

			if (!Throttled(completed.NodeId, settings.SessionCompletionSoundThrottleSeconds))
				_player.Play(settings.SessionCompletionSoundFile);

			// A throttled sound is acknowledged too: staying silent was this client's decision, and the host would not play it either.
			await _client.NotificationSoundAckAsync(completed.NodeId).ConfigureAwait(false);
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Handling the completion sound of {NodeId} failed", completed.NodeId);
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
			_logger.LogInformation("Could not read the completion sound option of {NodeId} ({Message}); playing", nodeId, ex.Message);
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

	private bool Throttled(string nodeId, int throttleSeconds)
	{
		var now = _clock.GetUtcNow();
		var window = TimeSpan.FromSeconds(Math.Max(0, throttleSeconds));
		lock (_lock)
		{
			if (_lastPlayed.TryGetValue(nodeId, out var last) && now - last < window)
				return true;

			_lastPlayed[nodeId] = now;
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
