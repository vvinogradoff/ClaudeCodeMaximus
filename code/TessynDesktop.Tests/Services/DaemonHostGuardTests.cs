using System.Diagnostics;
using TessynDesktop.Models.Remote;
using TessynDesktop.Services;

namespace TessynDesktop.Tests.Services;

/// <summary>One daemon host per user session, and the <c>--background</c> start mode (decision A23 Q6). Every test uses its own mutex name, never the real one.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class DaemonHostGuardTests
{
	private static string UniqueName() => @"Local\TessynDesktop.Test." + Guid.NewGuid().ToString("N");

	[Fact]
	public void TheFirstGuardWins_AndTheSecondMustExit()
	{
		var name = UniqueName();
		using var first = new DaemonHostGuard(name);
		using var second = new DaemonHostGuard(name);

		Assert.True(first.TryAcquire());
		Assert.False(second.TryAcquire());
		Assert.True(DaemonHostGuard.IsHeldElsewhere(name));
	}

	[Fact]
	public void TheGuardIsFreeAgainWhenTheHostEnds()
	{
		var name = UniqueName();
		var first = new DaemonHostGuard(name);
		Assert.True(first.TryAcquire());
		first.Dispose();

		using var next = new DaemonHostGuard(name);
		Assert.True(next.TryAcquire());
		Assert.False(DaemonHostGuard.IsHeldElsewhere(UniqueName()));
	}

	[Fact]
	public void AcquiringTwiceInOneProcessIsHarmless()
	{
		using var guard = new DaemonHostGuard(UniqueName());

		Assert.True(guard.TryAcquire());
		Assert.True(guard.TryAcquire());
	}

	[Fact]
	public async Task AGuardHeldInAnotherProcess_BlocksThisOne()
	{
		var name = UniqueName();
		var marker = Path.Combine(Path.GetTempPath(), "tessyn-guard-" + Guid.NewGuid().ToString("N"));
		var script = $"$m = New-Object System.Threading.Mutex($true, '{name}'); New-Item -ItemType File -Path '{marker}' | Out-Null; Start-Sleep -Seconds 20";
		using var holder = Process.Start(new ProcessStartInfo("powershell.exe", $"-NoProfile -NonInteractive -Command \"{script}\"") { UseShellExecute = false, CreateNoWindow = true })!;
		try
		{
			var deadline = DateTime.UtcNow.AddSeconds(30);
			while (!File.Exists(marker) && DateTime.UtcNow < deadline)
				await Task.Delay(50);

			Assert.True(File.Exists(marker), "the holder process did not take the mutex in time");
			using var guard = new DaemonHostGuard(name);
			Assert.False(guard.TryAcquire());
		}
		finally
		{
			holder.Kill(entireProcessTree: true);
			holder.WaitForExit();
			File.Delete(marker);
		}
	}

	[Fact]
	public void TheDefaultNameIsTheOneTheWpfClientLooksAt()
	{
		Assert.Equal(RemoteProtocol.Discovery.HostMutexName, DaemonHostGuard.DefaultName);
		Assert.StartsWith(@"Local\", DaemonHostGuard.DefaultName); // one host per logged-on user
	}

	[Theory]
	[InlineData(new string[0], false)]
	[InlineData(new[] { "--background" }, true)]
	[InlineData(new[] { "--BACKGROUND" }, true)]
	[InlineData(new[] { "--other", "--background" }, true)]
	[InlineData(new[] { "--back" }, false)]
	public void Background_IsRecognisedFromTheArguments(string[] args, bool expected)
	{
		Assert.Equal(expected, BackgroundHostOptions.Parse(args).IsBackground);
	}

	[Fact]
	public void TheIsolationOptions_AreParsed_AndTheNamesGetTheInstanceSuffix()
	{
		var options = BackgroundHostOptions.Parse(["--background", "--data-root", "D:/root", "--daemon-file", "D:/d.json", "--instance", "abc"]);

		Assert.Equal(("D:/root", "D:/d.json", "abc"), (options.DataRoot, options.DaemonFile, options.Instance));
		Assert.Equal(RemoteProtocol.Discovery.HostMutexName + ".abc", options.GuardName);
		Assert.Equal(RemoteProtocol.Discovery.HostExitEventName + ".abc", options.ExitEventName);
	}

	[Fact]
	public void WithoutIsolationOptions_TheNamesAreTheRealHostsNames()
	{
		var options = BackgroundHostOptions.Parse(["--background", "--data-root"]);   // a value is missing: ignored

		Assert.Null(options.DataRoot);
		Assert.Equal(RemoteProtocol.Discovery.HostMutexName, options.GuardName);
		Assert.Equal(RemoteProtocol.Discovery.HostExitEventName, options.ExitEventName);
	}
}
