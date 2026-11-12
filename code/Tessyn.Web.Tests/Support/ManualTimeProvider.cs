namespace Tessyn.Web.Tests.Support;

/// <remarks>Created by Claude</remarks>
public sealed class ManualTimeProvider : TimeProvider
{
	private DateTimeOffset _now = new(2026, 11, 12, 12, 0, 0, TimeSpan.Zero);

	public override DateTimeOffset GetUtcNow() => _now;

	public void Advance(TimeSpan by)
	{
		_now += by;
	}
}
