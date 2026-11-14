using TessynDesktop.WPF.Infrastructure;

namespace TessynDesktop.WPF.Tests;

/// <summary>Command-line overrides used by automated runs.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class StartupOptionsTests
{
	[Fact]
	public void Parse_ReadsDaemonFileAndDataRoot()
	{
		var options = StartupOptions.Parse(["--daemon-file", @"C:\x\daemon.json", "--data-root", @"C:\x\data"]);

		Assert.Equal(@"C:\x\daemon.json", options.DaemonFile);
		Assert.Equal(@"C:\x\data", options.DataRoot);
	}

	[Fact]
	public void Parse_WithoutArguments_UsesDefaultsAndTheStandardMutex()
	{
		var options = StartupOptions.Parse([]);

		Assert.Null(options.DaemonFile);
		Assert.Null(options.DataRoot);
		Assert.Equal(Constants.Identity.SingleInstanceMutexName, options.MutexName);
	}

	[Fact]
	public void CustomDataRoot_GetsItsOwnMutexSoTestRunsNeverCollideWithTheRealApp()
	{
		var a = StartupOptions.Parse(["--data-root", @"C:\a"]);
		var b = StartupOptions.Parse(["--data-root", @"C:\b"]);

		Assert.NotEqual(Constants.Identity.SingleInstanceMutexName, a.MutexName);
		Assert.NotEqual(a.MutexName, b.MutexName);
	}

	[Fact]
	public void Parse_RecognizesTheTestWindowSwitch_AnywhereInTheArguments()
	{
		Assert.True(StartupOptions.Parse(["--test-window"]).TestWindow);
		Assert.True(StartupOptions.Parse(["--data-root", "x", "--test-window"]).TestWindow);
		Assert.False(StartupOptions.Parse(["--data-root", "x"]).TestWindow);
	}

	[Fact]
	public void Parse_IgnoresATrailingOptionWithoutValue()
	{
		Assert.Null(StartupOptions.Parse(["--daemon-file"]).DaemonFile);
	}
}
