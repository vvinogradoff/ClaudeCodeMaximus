using System.Diagnostics;

namespace TessynDesktop.WPF.E2E.Tests;

/// <summary>Starts the built app (off-screen, not activated: --test-window) and checks that closing its window really ends the process (review finding F1).</summary>
/// <remarks>Created by Claude</remarks>
[Collection("timing-sensitive")]
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

	private readonly AppWindowFinder _windows = new();

	[Fact]
	public async Task ClosingTheMainWindow_ExitsTheProcess()
	{
		using var process = Start();
		try
		{
			Assert.True(await _windows.WaitForWindowAsync(process, TimeSpan.FromSeconds(30)), "The main window did not appear.");
			await Task.Delay(1500);

			Assert.True(_windows.Close(process));

			var exited = process.WaitForExit(TimeSpan.FromSeconds(15));
			Assert.True(exited, "The process is still alive 15 s after the main window was closed (shutdown deadlock).");
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
			Assert.True(await _windows.WaitForWindowAsync(first, TimeSpan.FromSeconds(30)));
			_windows.Close(first);
			Assert.True(first.WaitForExit(TimeSpan.FromSeconds(15)));
		}

		using var second = Start();
		try
		{
			Assert.True(await _windows.WaitForWindowAsync(second, TimeSpan.FromSeconds(30)), "The second launch did not show a window.");
			_windows.Close(second);
			Assert.True(second.WaitForExit(TimeSpan.FromSeconds(15)));
		}
		finally
		{
			if (!second.HasExited)
				second.Kill(entireProcessTree: true);
		}
	}
}
