using System;
using System.Collections.Concurrent;
using System.Threading;

namespace TessynDesktop.Services;

/// <inheritdoc cref="INotificationAckBook" />
/// <remarks>Created by Claude</remarks>
public sealed class NotificationAckBook : INotificationAckBook
{
	private const int MaxRememberedSoundAcks = 256;

	private readonly TimeProvider _time;
	private readonly ConcurrentDictionary<string, ToastWait> _toasts = new(StringComparer.Ordinal);
	private readonly ConcurrentDictionary<string, DateTimeOffset> _sounds = new(StringComparer.Ordinal);

	public NotificationAckBook(TimeProvider? time = null) => _time = time ?? TimeProvider.System;

	/// <inheritdoc />
	public IToastAckWait Expect(string notificationId)
	{
		var wait = new ToastWait(this, notificationId);
		_toasts[notificationId] = wait;
		return wait;
	}

	/// <inheritdoc />
	public bool AcknowledgeToast(string notificationId)
	{
		if (!_toasts.TryGetValue(notificationId, out var wait))
			return false;

		wait.Set();
		return true;
	}

	/// <inheritdoc />
	public void AcknowledgeSound(string nodeId)
	{
		if (string.IsNullOrEmpty(nodeId))
			return;

		// Bounded: a client cannot make this grow without limit by acknowledging ids that never play.
		if (_sounds.Count >= MaxRememberedSoundAcks)
			_sounds.Clear();

		_sounds[nodeId] = _time.GetUtcNow();
	}

	/// <inheritdoc />
	public bool TryConsumeSoundAck(string nodeId, DateTimeOffset since) =>
		_sounds.TryGetValue(nodeId, out var at) && at >= since && _sounds.TryRemove(new System.Collections.Generic.KeyValuePair<string, DateTimeOffset>(nodeId, at));

	private sealed class ToastWait(NotificationAckBook owner, string id) : IToastAckWait
	{
		private readonly ManualResetEventSlim _event = new(false);

		public void Set()
		{
			try { _event.Set(); }
			catch (ObjectDisposedException) { /* the wait just ended */ }
		}

		public bool Wait(TimeSpan timeout) => _event.Wait(timeout);

		public void Dispose()
		{
			owner._toasts.TryRemove(new System.Collections.Generic.KeyValuePair<string, ToastWait>(id, this));
			_event.Dispose();
		}
	}
}
