using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Tessyn.Web.Models;
using Tessyn.Web.Options;

namespace Tessyn.Web.Link;

/// <summary>
/// Singleton relay core: the one daemon link, pending request map, browser event fan-out and the resume ring buffer.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class DaemonLinkHub
{
	private readonly IOptions<TessynWebOptions> _options;
	private readonly TimeProvider _time;
	private readonly WireJson _json;
	private readonly RemoteMethodAllowlist _allowlist;
	private readonly ILogger<DaemonLinkHub> _logger;

	private readonly object _gate = new();
	private readonly Queue<BufferedEvent> _buffer = new();
	private readonly Dictionary<long, EventSubscription> _subscribers = new();
	private readonly ConcurrentDictionary<string, PendingRequest> _pending = new();

	private DaemonLink? _current;
	private DateTimeOffset _statusSince;
	private long _latestSeq;
	private long _nextSubscriberId;
	private long _nextRequestId;

	public DaemonLinkHub(
		IOptions<TessynWebOptions> options,
		TimeProvider time,
		WireJson json,
		RemoteMethodAllowlist allowlist,
		ILogger<DaemonLinkHub> logger)
	{
		_options = options;
		_time = time;
		_json = json;
		_allowlist = allowlist;
		_logger = logger;
		_statusSince = time.GetUtcNow();
	}

	public bool IsOnline
	{
		get
		{
			lock (_gate)
				return _current is not null;
		}
	}

	/// <summary>Registers a new daemon link; a previous link is superseded (completed, its pending requests failed).</summary>
	public DaemonLink Connect(string daemonName)
	{
		var name = SanitizeName(daemonName);
		DaemonLink? old;
		DaemonLink link;
		lock (_gate)
		{
			old = _current;
			link = new DaemonLink(name, _time.GetUtcNow());
			_current = link;
			_statusSince = link.Since;
			PublishLocked(Constants.Notifications.LinkStatus, _json.Serialize(new LinkStatusParams(true, _statusSince)));
		}

		if (old is not null)
		{
			old.Complete();
			FailPending(old);
			_logger.LogInformation("Daemon link superseded by a new connection from {Daemon}", name);
		}
		else
		{
			_logger.LogInformation("Daemon link connected from {Daemon}", name);
		}

		return link;
	}

	/// <summary>Called when a link's stream ends; a superseded link is ignored.</summary>
	public void Disconnect(DaemonLink link)
	{
		var wasCurrent = false;
		lock (_gate)
		{
			if (ReferenceEquals(_current, link))
			{
				wasCurrent = true;
				_current = null;
				_statusSince = _time.GetUtcNow();
				PublishLocked(Constants.Notifications.LinkStatus, _json.Serialize(new LinkStatusParams(false, _statusSince)));
			}
		}

		link.Complete();
		FailPending(link);
		if (wasCurrent)
			_logger.LogInformation("Daemon link disconnected ({Daemon})", link.DaemonName);
	}

	/// <summary>Sends a request to the daemon and waits for its reply, honouring the per-method timeout.</summary>
	public async Task<RpcOutcome> SendAsync(string method, JsonElement? paramsElement, CancellationToken cancellationToken, TimeSpan? timeout = null)
	{
		PendingRequest pending;
		lock (_gate)
		{
			if (_current is null)
				return _json.ErrorOutcome(Constants.JsonRpc.DaemonOffline, Constants.JsonRpc.DaemonOfflineMessage);

			var id = "r-" + Interlocked.Increment(ref _nextRequestId);
			pending = new PendingRequest(id, _current);
			_pending[id] = pending;
			var frame = _json.BuildRequestFrame(id, method, paramsElement);
			if (!_current.TryEnqueue(frame))
			{
				_pending.TryRemove(id, out _);
				return _json.ErrorOutcome(Constants.JsonRpc.DaemonOffline, Constants.JsonRpc.DaemonOfflineMessage);
			}
		}

		var link = _options.Value.Link;
		var seconds = _allowlist.IsLong(method) ? link.LongRequestTimeoutSeconds : link.RequestTimeoutSeconds;
		try
		{
			return await pending.Completion.Task.WaitAsync(timeout ?? TimeSpan.FromSeconds(seconds), cancellationToken).ConfigureAwait(false);
		}
		catch (TimeoutException)
		{
			_pending.TryRemove(pending.Id, out _);
			_logger.LogWarning("Daemon request {Id} ({Method}) timed out", pending.Id, method);
			return _json.ErrorOutcome(Constants.JsonRpc.DaemonTimeout, Constants.JsonRpc.DaemonTimeoutMessage);
		}
		catch (OperationCanceledException)
		{
			_pending.TryRemove(pending.Id, out _);
			throw;
		}
	}

	/// <summary>Completes a pending request with the daemon's reply; unknown ids (late replies) are ignored.</summary>
	public bool CompleteReply(string id, RpcOutcome outcome)
	{
		if (!_pending.TryRemove(id, out var pending))
			return false;

		return pending.Completion.TrySetResult(outcome);
	}

	/// <summary>Records a daemon notification and fans it out to browsers.</summary>
	public long Publish(string method, JsonElement? paramsElement)
	{
		// Re-serialise (compact) so no raw newline can ever break SSE framing.
		string? paramsJson = paramsElement is { ValueKind: not JsonValueKind.Undefined } p ? JsonSerializer.Serialize(p) : null;
		lock (_gate)
			return PublishLocked(method, paramsJson);
	}

	/// <summary>Subscribes a browser; the initial frames are link.status, then resync or the replayed events.</summary>
	public EventSubscription Subscribe(long? lastEventId)
	{
		lock (_gate)
		{
			PruneLocked();
			var initial = new List<BrowserFrame>
			{
				new(null, _json.BuildNotificationFrame(Constants.Notifications.LinkStatus, _json.Serialize(CurrentStatusLocked()))),
			};

			if (lastEventId.HasValue)
				AddResumeFramesLocked(initial, lastEventId.Value);

			var subscription = new EventSubscription(++_nextSubscriberId, initial);
			_subscribers[subscription.Id] = subscription;
			return subscription;
		}
	}

	public void Unsubscribe(EventSubscription subscription)
	{
		lock (_gate)
			_subscribers.Remove(subscription.Id);

		subscription.Complete();
	}

	private void AddResumeFramesLocked(List<BrowserFrame> initial, long lastEventId)
	{
		if (lastEventId == _latestSeq)
			return;

		var firstBuffered = _buffer.Count > 0 ? _buffer.Peek().Seq : _latestSeq + 1;
		var needsResync = lastEventId > _latestSeq || firstBuffered > lastEventId + 1;
		if (needsResync)
		{
			initial.Add(new BrowserFrame(_latestSeq, _json.BuildNotificationFrame(Constants.Notifications.Resync, "{}")));
			return;
		}

		foreach (var buffered in _buffer)
		{
			if (buffered.Seq > lastEventId)
				initial.Add(new BrowserFrame(buffered.Seq, buffered.Data));
		}
	}

	private LinkStatusParams CurrentStatusLocked()
	{
		return new LinkStatusParams(_current is not null, _statusSince);
	}

	private long PublishLocked(string method, string? paramsJson)
	{
		var seq = ++_latestSeq;
		var data = _json.BuildNotificationFrame(method, paramsJson);
		_buffer.Enqueue(new BufferedEvent(seq, _time.GetUtcNow(), data));
		PruneLocked();

		var frame = new BrowserFrame(seq, data);
		List<EventSubscription>? slow = null;
		foreach (var subscriber in _subscribers.Values)
		{
			if (subscriber.TryPush(frame))
				continue;

			slow ??= new List<EventSubscription>();
			slow.Add(subscriber);
		}

		if (slow is null)
			return seq;

		foreach (var subscriber in slow)
			_subscribers.Remove(subscriber.Id);

		return seq;
	}

	private void PruneLocked()
	{
		var link = _options.Value.Link;
		var cutoff = _time.GetUtcNow() - TimeSpan.FromMinutes(link.RingBufferMinutes);
		while (_buffer.Count > 0 && (_buffer.Count > link.RingBufferEvents || _buffer.Peek().At < cutoff))
			_buffer.Dequeue();
	}

	private void FailPending(DaemonLink link)
	{
		var offline = _json.ErrorOutcome(Constants.JsonRpc.DaemonOffline, Constants.JsonRpc.DaemonOfflineMessage);
		foreach (var pending in _pending.Values)
		{
			if (!ReferenceEquals(pending.Link, link))
				continue;

			if (_pending.TryRemove(pending.Id, out var removed))
				removed.Completion.TrySetResult(offline);
		}
	}

	private static string SanitizeName(string name)
	{
		var cleaned = new string(name.Where(c => !char.IsControl(c)).ToArray()).Trim();
		if (cleaned.Length > Constants.Limits.DaemonNameMaxLength)
			cleaned = cleaned[..Constants.Limits.DaemonNameMaxLength];

		return cleaned.Length == 0 ? "unknown" : cleaned;
	}
}
