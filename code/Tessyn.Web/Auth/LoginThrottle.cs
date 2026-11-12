using Microsoft.Extensions.Options;
using Tessyn.Web.Options;

namespace Tessyn.Web.Auth;

/// <summary>Sliding-window failure counters per client IP and globally.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class LoginThrottle
{
	private readonly IOptions<TessynWebOptions> _options;
	private readonly TimeProvider _time;
	private readonly object _lock = new();
	private readonly Dictionary<string, List<DateTimeOffset>> _perIp = new();
	private readonly List<DateTimeOffset> _global = new();

	public LoginThrottle(IOptions<TessynWebOptions> options, TimeProvider time)
	{
		_options = options;
		_time = time;
	}

	/// <summary>Returns the time to wait before another attempt is allowed, or null when allowed now.</summary>
	public TimeSpan? GetRetryAfter(string ip)
	{
		var throttle = _options.Value.Auth.Throttle;
		lock (_lock)
		{
			var now = _time.GetUtcNow();
			var ipWait = WaitFor(GetIpList(ip), now, throttle.PerIpFailures, TimeSpan.FromMinutes(throttle.PerIpWindowMinutes));
			var globalWait = WaitFor(_global, now, throttle.GlobalFailures, TimeSpan.FromMinutes(throttle.GlobalWindowMinutes));

			if (ipWait is null)
				return globalWait;
			if (globalWait is null)
				return ipWait;
			return ipWait > globalWait ? ipWait : globalWait;
		}
	}

	public void RecordFailure(string ip)
	{
		lock (_lock)
		{
			var now = _time.GetUtcNow();
			GetIpList(ip).Add(now);
			_global.Add(now);
		}
	}

	public void RecordSuccess(string ip)
	{
		lock (_lock)
		{
			_perIp.Remove(ip);
		}
	}

	private List<DateTimeOffset> GetIpList(string ip)
	{
		if (!_perIp.TryGetValue(ip, out var list))
		{
			list = new List<DateTimeOffset>();
			_perIp[ip] = list;
		}

		return list;
	}

	private static TimeSpan? WaitFor(List<DateTimeOffset> failures, DateTimeOffset now, int limit, TimeSpan window)
	{
		failures.RemoveAll(t => now - t >= window);
		if (failures.Count < limit)
			return null;

		var blockingFailure = failures[failures.Count - limit];
		var wait = blockingFailure + window - now;
		return wait > TimeSpan.Zero ? wait : TimeSpan.FromSeconds(1);
	}
}
