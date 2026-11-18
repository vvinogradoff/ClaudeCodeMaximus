using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.Services;

/// <summary>
/// Wraps the host's <see cref="ISessionSoundService"/> (package J2). While a connected client presents sounds it plays the Focus item sound itself
/// (on <c>focus.itemCreated</c>) and acknowledges it with <c>notification.ack { nodeId }</c>; the host plays it only when no such acknowledgement came
/// within <see cref="Constants.Composer.NotificationAckTimeout"/> (V3), so a hung or killed client does not lose the sound. The wait is in the
/// background: the caller (the desktop's UI thread) is never held. The Settings "Test" button always plays.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class PresenceAwareSessionSoundService : ISessionSoundService
{
	private readonly ISessionSoundService _host;
	private readonly IClientPresenceRegistry _presence;
	private readonly INotificationAckBook _acks;
	private readonly TimeProvider _time;
	private readonly TimeSpan _ackTimeout;

	public PresenceAwareSessionSoundService(
		ISessionSoundService host, IClientPresenceRegistry presence, INotificationAckBook acks, TimeProvider? time = null, TimeSpan? ackTimeout = null)
	{
		_host       = host;
		_presence   = presence;
		_acks       = acks;
		_time       = time ?? TimeProvider.System;
		_ackTimeout = ackTimeout ?? Constants.Composer.NotificationAckTimeout;
	}

	/// <inheritdoc />
	public void PlayFocusItemSound(string nodeId)
	{
		if (!_presence.IsPresented(RemoteProtocol.Notifications.SoundChannel))
		{
			_host.PlayFocusItemSound(nodeId);
			return;
		}

		// The client may have played (and acknowledged) already, when the event reached it before this call.
		var since = _time.GetUtcNow() - _ackTimeout;
		if (_acks.TryConsumeSoundAck(nodeId, since))
			return;

		_ = Task.Run(async () =>
		{
			await Task.Delay(_ackTimeout, _time);
			if (!_acks.TryConsumeSoundAck(nodeId, since))
				_host.PlayFocusItemSound(nodeId);
		});
	}

	/// <inheritdoc />
	public void PlayTestSound(string filePath) => _host.PlayTestSound(filePath);

	/// <inheritdoc />
	public IReadOnlyList<string> GetAvailableSounds() => _host.GetAvailableSounds();
}
