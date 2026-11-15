using System.Diagnostics;

namespace TessynDesktop.WPF.E2E.Tests;

/// <summary>
/// The BUILT app, started twice on one data root: the second process must hand over to the first and exit, the first must stay up
/// (W0, single instance). Both run with --test-window, so the first one ignores the activation request and takes no focus.
/// </summary>
/// <remarks>Created by Claude</remarks>
[Collection("wpf-app-e2e")]
public sealed class WpfSingleInstanceE2ETests : IDisposable
{
	private readonly string _root = Path.Combine(Path.GetTempPath(), "tessyn-wpf-single-" + Guid.NewGuid().ToString("N"));
	private readonly AppWindowFinder _windows = new();

	public void Dispose()
	{
		try
		{
			if (Directory.Exists(_root))
				Directory.Delete(_root, recursive: true);
		}
		catch (IOException)
		{
			// WebView2 helper processes may still hold the folder for a moment; the temp folder is disposable.
		}
	}

	private Process Start()
	{
		var exe = new BuiltAppLocator().FindExe();
		Directory.CreateDirectory(_root);
		var info = new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe)! };
		info.ArgumentList.Add("--test-window");
		info.ArgumentList.Add("--data-root");
		info.ArgumentList.Add(_root);
		info.ArgumentList.Add("--daemon-file");
		info.ArgumentList.Add(Path.Combine(_root, "no-such-daemon.json"));
		return Process.Start(info)!;
	}

	[Fact]
	public async Task ASecondStartOnTheSameDataRoot_ExitsAndLeavesTheFirstRunning()
	{
		using var first = Start();
		try
		{
			Assert.True(await _windows.WaitForWindowAsync(first, TimeSpan.FromSeconds(30)), "The main window did not appear.");

			using var second = Start();
			var exited = second.WaitForExit(TimeSpan.FromSeconds(20));
			if (!exited)
				second.Kill(entireProcessTree: true);

			Assert.True(exited, "The second instance did not exit.");
			await Task.Delay(500);
			Assert.False(first.HasExited, "The first instance went away when a second was started.");
			Assert.NotEqual(IntPtr.Zero, _windows.Find(first.Id));
		}
		finally
		{
			if (!first.HasExited)
				first.Kill(entireProcessTree: true);
		}
	}
}
