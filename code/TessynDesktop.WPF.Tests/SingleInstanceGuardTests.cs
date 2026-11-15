using TessynDesktop.WPF.Services;

namespace TessynDesktop.WPF.Tests;

/// <summary>One app per data root; a second start asks the first to activate (W0).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class SingleInstanceGuardTests
{
	private static string UniqueName() => @"Local\TessynDesktop.WPF.Tests." + Guid.NewGuid().ToString("N");

	[Fact]
	public void TheFirstInstanceAcquires_TheSecondDoesNotAndSignalsTheFirst()
	{
		var name = UniqueName();
		using var first = new SingleInstanceGuard(name);
		var activated = new ManualResetEventSlim();
		Assert.True(first.TryAcquire());
		first.ListenForActivation(activated.Set);

		using var second = new SingleInstanceGuard(name);
		Assert.False(second.TryAcquire());

		Assert.True(activated.Wait(TimeSpan.FromSeconds(5)), "The first instance was not asked to activate itself");
	}

	[Fact]
	public void EveryLaterStartSignalsAgain()
	{
		var name = UniqueName();
		using var first = new SingleInstanceGuard(name);
		var count = 0;
		var twice = new CountdownEvent(2);
		Assert.True(first.TryAcquire());
		first.ListenForActivation(() =>
		{
			Interlocked.Increment(ref count);
			twice.Signal();
		});

		for (var i = 0; i < 2; i++)
		{
			using var later = new SingleInstanceGuard(name);
			Assert.False(later.TryAcquire());
			Thread.Sleep(200);
		}

		Assert.True(twice.Wait(TimeSpan.FromSeconds(5)));
		Assert.Equal(2, count);
	}

	[Fact]
	public void DifferentNames_DoNotCollide()
	{
		using var one = new SingleInstanceGuard(UniqueName());
		using var other = new SingleInstanceGuard(UniqueName());

		Assert.True(one.TryAcquire());
		Assert.True(other.TryAcquire());
	}

	[Fact]
	public void AfterTheFirstInstanceIsDisposed_ANewOneCanStart()
	{
		var name = UniqueName();
		var first = new SingleInstanceGuard(name);
		Assert.True(first.TryAcquire());
		first.Dispose();

		using var next = new SingleInstanceGuard(name);

		Assert.True(next.TryAcquire());
	}
}
