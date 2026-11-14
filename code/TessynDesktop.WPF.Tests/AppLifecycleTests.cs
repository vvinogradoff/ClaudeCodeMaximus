using System.Diagnostics;

namespace TessynDesktop.WPF.Tests;

/// <summary>Starts the built app and checks that closing its window really ends the process (review finding F1).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class AppLifecycleTests : IDisposable
{
	private readonly string _root = Path.Combine(Path.GetTempPath(), "tessyn-wpf-app-" + Guid.NewGuid().ToString("N"));

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
		var exe = Path.Combine(AppContext.BaseDirectory, "TessynDesktop.WPF.exe");
		Assert.True(File.Exists(exe), "The WPF exe was not copied next to the tests: " + exe);
		Directory.CreateDirectory(_root);
		var info = new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = AppContext.BaseDirectory };
		info.ArgumentList.Add("--data-root");
		info.ArgumentList.Add(_root);
		info.ArgumentList.Add("--daemon-file");
		info.ArgumentList.Add(Path.Combine(_root, "no-such-daemon.json"));
		return Process.Start(info)!;
	}

	private static async Task<bool> WaitForWindowAsync(Process process, TimeSpan timeout)
	{
		var deadline = DateTime.UtcNow + timeout;
		while (DateTime.UtcNow < deadline && !process.HasExited)
		{
			process.Refresh();
			if (process.MainWindowHandle != IntPtr.Zero)
				return true;
			await Task.Delay(100);
		}

		return false;
	}

	[Fact]
	public async Task ClosingTheMainWindow_ExitsTheProcessAndWritesTheState()
	{
		using var process = Start();
		try
		{
			Assert.True(await WaitForWindowAsync(process, TimeSpan.FromSeconds(30)), "The main window did not appear.");
			await Task.Delay(1500);

			Assert.True(process.CloseMainWindow());

			var exited = process.WaitForExit(TimeSpan.FromSeconds(15));
			Assert.True(exited, "The process is still alive 15 s after the main window was closed (shutdown deadlock).");
			Assert.True(File.Exists(Path.Combine(_root, "clients", "wpf.json")), "The client state was not flushed at exit.");
		}
		finally
		{
			if (!process.HasExited)
				process.Kill(entireProcessTree: true);
		}
	}

	[Fact]
	public async Task AfterAnExit_TheNextLaunchStartsInsteadOfExitingSilently()
	{
		using (var first = Start())
		{
			Assert.True(await WaitForWindowAsync(first, TimeSpan.FromSeconds(30)));
			first.CloseMainWindow();
			Assert.True(first.WaitForExit(TimeSpan.FromSeconds(15)));
		}

		using var second = Start();
		try
		{
			Assert.True(await WaitForWindowAsync(second, TimeSpan.FromSeconds(30)), "The second launch did not show a window.");
			second.CloseMainWindow();
			Assert.True(second.WaitForExit(TimeSpan.FromSeconds(15)));
		}
		finally
		{
			if (!second.HasExited)
				second.Kill(entireProcessTree: true);
		}
	}
}
