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
		for (var i = 0; i < 4; i++)
			throttle.RecordFailure("1.1.1.1");
		Assert.Null(throttle.GetRetryAfter("1.1.1.1"));

		throttle.RecordFailure("1.1.1.1");
		var wait = throttle.GetRetryAfter("1.1.1.1");

		Assert.NotNull(wait);
		Assert.Equal(TimeSpan.FromMinutes(15), wait);
		Assert.Null(throttle.GetRetryAfter("2.2.2.2"));
	}

	[Fact]
	public void Failures_expire_after_the_window()
	{
		var throttle = Create();
		for (var i = 0; i < 5; i++)
			throttle.RecordFailure("1.1.1.1");

		_time.Advance(TimeSpan.FromMinutes(15));

		Assert.Null(throttle.GetRetryAfter("1.1.1.1"));
	}

	[Fact]
	public void Success_clears_the_ip_but_not_the_global_count()
	{
		var throttle = Create(o => o.GlobalFailures = 3);
		throttle.RecordFailure("1.1.1.1");
		throttle.RecordFailure("1.1.1.1");
		throttle.RecordSuccess("1.1.1.1");
		throttle.RecordFailure("2.2.2.2");

		Assert.NotNull(throttle.GetRetryAfter("3.3.3.3"));
		Assert.Equal(TimeSpan.FromMinutes(60), throttle.GetRetryAfter("3.3.3.3"));
	}
}
