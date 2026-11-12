using System.Net;
using Microsoft.Extensions.Options;
using Tessyn.Web.Options;

namespace Tessyn.Web.Auth;

/// <summary>Sliding-window attempt counters per client IP (IPv6 by /64) and globally.</summary>
/// <remarks>
/// An attempt is recorded BEFORE the password is verified (so parallel guesses all count) and removed again on success.
/// Created by Claude
/// </remarks>
public sealed class LoginThrottle
{
	private readonly IOptions<TessynWebOptions> _options;
	private readonly TimeProvider _time;
	private readonly object _lock = new();
	private readonly Dictionary<string, List<Attempt>> _perIp = new();
	private readonly List<Attempt> _global = new();
	private DateTimeOffset _lastSweep;

	public LoginThrottle(IOptions<TessynWebOptions> options, TimeProvider time)
	{
		_options = options;
		_time = time;
		_lastSweep = time.GetUtcNow();
	}

	/// <summary>One recorded login attempt.</summary>
	public sealed class Attempt
	{
		internal Attempt(string key, DateTimeOffset at)
		{
			Key = key;
			At = at;
		}

		internal string Key { get; }
		internal DateTimeOffset At { get; }
	}

	/// <summary>Outcome of <see cref="Begin"/>: either blocked (RetryAfter set) or allowed with a recorded attempt.</summary>
	public sealed record Decision(TimeSpan? RetryAfter, Attempt? Attempt);

	/// <summary>The key an address is throttled under: IPv4 as is, IPv6 reduced to its /64.</summary>
	public static string KeyFor(string ip)
	{
		if (!IPAddress.TryParse(ip, out var address))
			return ip;

		if (address.IsIPv4MappedToIPv6)
			return address.MapToIPv4().ToString();

		if (address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetworkV6)
			return address.ToString();

		var bytes = address.GetAddressBytes();
		Array.Clear(bytes, Constants.Limits.IpV6PrefixBytes, bytes.Length - Constants.Limits.IpV6PrefixBytes);
		return new IPAddress(bytes) + "/64";
	}

	/// <summary>
	/// Checks the limits and, when allowed, records an attempt atomically.
	/// A trusted device is exempt from the global limit (the per-IP limit still applies).
	/// </summary>
	public Decision Begin(string ip, bool trustedDevice)
	{
		var throttle = _options.Value.Auth.Throttle;
		var key = KeyFor(ip);
		lock (_lock)
		{
			var now = _time.GetUtcNow();
			Sweep(now, throttle);

			_perIp.TryGetValue(key, out var ipList);
			var ipWait = ipList is null
				? null
				: WaitFor(ipList, now, throttle.PerIpFailures, TimeSpan.FromMinutes(throttle.PerIpWindowMinutes));
			var globalWait = trustedDevice
				? null
				: WaitFor(_global, now, throttle.GlobalFailures, TimeSpan.FromMinutes(throttle.GlobalWindowMinutes));

			if (ipWait is not null || globalWait is not null)
			{
				var wait = ipWait is null ? globalWait : globalWait is null ? ipWait : (ipWait > globalWait ? ipWait : globalWait);
				return new Decision(wait, null);
			}

			var attempt = new Attempt(key, now);
			if (ipList is null)
			{
				ipList = new List<Attempt>();
				_perIp[key] = ipList;
			}

			ipList.Add(attempt);
			_global.Add(attempt);
			return new Decision(null, attempt);
		}
	}

	/// <summary>Successful login: forget this attempt and the IP's earlier failures (the global count keeps them).</summary>
	public void Succeed(Attempt attempt)
	{
		lock (_lock)
		{
			_global.Remove(attempt);
			_perIp.Remove(attempt.Key);
		}
	}

	/// <summary>Number of per-IP buckets currently held (diagnostics / tests).</summary>
	public int TrackedIpCount
	{
		get
		{
			lock (_lock)
				return _perIp.Count;
		}
	}

	private void Sweep(DateTimeOffset now, ThrottleOptions throttle)
	{
		if (now - _lastSweep < TimeSpan.FromMinutes(Constants.Limits.ThrottleSweepMinutes))
			return;

		_lastSweep = now;
		var window = TimeSpan.FromMinutes(throttle.PerIpWindowMinutes);
		foreach (var key in _perIp.Keys.ToList())
		{
			_perIp[key].RemoveAll(a => now - a.At >= window);
			if (_perIp[key].Count == 0)
				_perIp.Remove(key);
		}
	}

	private static TimeSpan? WaitFor(List<Attempt> attempts, DateTimeOffset now, int limit, TimeSpan window)
	{
		attempts.RemoveAll(a => now - a.At >= window);
		if (attempts.Count < limit)
			return null;

		var blocking = attempts[attempts.Count - limit];
		var wait = blocking.At + window - now;
		return wait > TimeSpan.Zero ? wait : TimeSpan.FromSeconds(1);
	}
}
