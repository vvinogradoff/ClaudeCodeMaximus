using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.State;

namespace TessynDesktop.WPF.Tests;

/// <summary>FR.29.21: a guard seeded with a restored place treats it as the last good one, so a first read taken while Windows is settling is not taken over.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class FloatingPlacementGuardSeedTests
{
	private static PreviewPlacementModel At(int left) => new() { Left = left, Top = 10, Width = 800, Height = 600, Dpi = 96 };

	[Fact]
	public void ASeededGuard_KeepsTheRestoredPlace_WhenTheFirstReadIsForced()
	{
		var guard = new FloatingPlacementGuard();
		guard.Seed(At(2100));

		var remembered = guard.Filter(At(200), settling: true);

		Assert.Equal(2100, remembered!.Left);
		Assert.Equal(2100, guard.Filter(At(200), settling: false)!.Left);   // the same place read again afterwards
		Assert.Equal(300, guard.Filter(At(300), settling: false)!.Left);   // the user moved it
	}

	[Fact]
	public void AnUnseededGuard_TakesTheFirstForcedRead()
	{
		Assert.Equal(200, new FloatingPlacementGuard().Filter(At(200), settling: true)!.Left);
	}
}
