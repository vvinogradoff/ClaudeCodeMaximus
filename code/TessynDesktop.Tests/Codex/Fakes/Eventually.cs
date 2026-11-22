using System;
using System.Threading.Tasks;

namespace TessynDesktop.Tests.Codex.Fakes;

/// <summary>Waits (in real time) for a condition that background work makes true.</summary>
/// <remarks>Created by Claude</remarks>
internal sealed class Eventually
{
	public async Task TrueAsync(Func<bool> condition, string because, TimeSpan? timeout = null)
	{
		var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));
		while (!condition())
		{
			if (DateTime.UtcNow > deadline)
				throw new TimeoutException($"Timed out waiting until {because}.");
			await Task.Delay(10);
		}
	}
}
