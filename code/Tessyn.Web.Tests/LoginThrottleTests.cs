using Microsoft.Extensions.Options;
using Tessyn.Web.Auth;
using Tessyn.Web.Options;
using Tessyn.Web.Tests.Support;

namespace Tessyn.Web.Tests;

/// <remarks>Created by Claude</remarks>
public sealed class LoginThrottleTests
{
	private readonly ManualTimeProvider _time = new();

	private LoginThrottle Create(Action<ThrottleOptions>? configure = null)
	{
		var options = new TessynWebOptions();
		configure?.Invoke(options.Auth.Throttle);
		return new LoginThrottle(Microsoft.Extensions.Options.Options.Create(options), _time);
	}

	[Fact]
	public void Blocks_at_the_per_ip_limit_and_reports_the_remaining_window()
	{
		var throttle = Create();
		for (var i = 0; i < 5; i++)
			Assert.Null(throttle.Begin("1.1.1.1", false).RetryAfter);

		var blocked = throttle.Begin("1.1.1.1", false);

		Assert.Equal(TimeSpan.FromMinutes(15), blocked.RetryAfter);
		Assert.Null(blocked.Attempt);
		Assert.Null(throttle.Begin("2.2.2.2", false).RetryAfter);
	}

	[Fact]
	public void Attempts_expire_after_the_window()
	{
		var throttle = Create();
		for (var i = 0; i < 5; i++)
			throttle.Begin("1.1.1.1", false);

		_time.Advance(TimeSpan.FromMinutes(15));

		Assert.Null(throttle.Begin("1.1.1.1", false).RetryAfter);
	}

	[Fact]
	public void Success_clears_the_ip_but_not_other_global_attempts()
	{
		var throttle = Create(o => o.GlobalFailures = 3);
		throttle.Begin("1.1.1.1", false);
		throttle.Begin("1.1.1.1", false);
		var success = throttle.Begin("1.1.1.1", false);
		throttle.Succeed(success.Attempt!);
		throttle.Begin("2.2.2.2", false);

		var blocked = throttle.Begin("3.3.3.3", false);

		Assert.Equal(TimeSpan.FromMinutes(60), blocked.RetryAfter);
	}

	[Fact]
	public void Parallel_attempts_are_counted_before_any_of_them_finishes()
	{
		var throttle = Create();
		var allowed = 0;

		Parallel.For(0, 200, new ParallelOptions { MaxDegreeOfParallelism = 32 }, _ =>
		{
			if (throttle.Begin("9.9.9.9", false).RetryAfter is null)
				Interlocked.Increment(ref allowed);
		});

		Assert.Equal(5, allowed);
	}

	[Fact]
	public void A_trusted_device_skips_the_global_limit_but_not_the_per_ip_limit()
	{
		var throttle = Create(o => { o.GlobalFailures = 3; o.PerIpFailures = 4; });
		for (var i = 0; i < 3; i++)
			throttle.Begin("7.7.7.7", false);

		Assert.NotNull(throttle.Begin("8.8.8.8", false).RetryAfter);
		for (var i = 0; i < 4; i++)
			Assert.Null(throttle.Begin("8.8.8.8", true).RetryAfter);
		Assert.NotNull(throttle.Begin("8.8.8.8", true).RetryAfter);
	}

	[Fact]
	public void IPv6_addresses_share_a_bucket_per_slash_64()
	{
		var throttle = Create();
		for (var i = 0; i < 5; i++)
			throttle.Begin("2001:db8:1:2::" + (i + 1), false);

		Assert.NotNull(throttle.Begin("2001:db8:1:2:ffff:ffff:ffff:ffff", false).RetryAfter);
		Assert.Null(throttle.Begin("2001:db8:1:3::1", false).RetryAfter);
		Assert.Equal("2001:db8:1:2::/64", LoginThrottle.KeyFor("2001:db8:1:2:aaaa:bbbb:cccc:dddd"));
		Assert.Equal("203.0.113.9", LoginThrottle.KeyFor("::ffff:203.0.113.9"));
		Assert.Equal("203.0.113.9", LoginThrottle.KeyFor("203.0.113.9"));
	}

	[Fact]
	public void Blocked_requests_do_not_create_entries_and_old_ones_are_swept()
	{
		var throttle = Create(o => o.GlobalFailures = 2);
		throttle.Begin("1.1.1.1", false);
		throttle.Begin("1.1.1.2", false);
		Assert.Equal(2, throttle.TrackedIpCount);

		for (var i = 0; i < 50; i++)
			Assert.NotNull(throttle.Begin("10.0.0." + i, false).RetryAfter);
		Assert.Equal(2, throttle.TrackedIpCount);

		_time.Advance(TimeSpan.FromMinutes(61));
		throttle.Begin("5.5.5.5", false);

		Assert.Equal(1, throttle.TrackedIpCount);
	}
}
