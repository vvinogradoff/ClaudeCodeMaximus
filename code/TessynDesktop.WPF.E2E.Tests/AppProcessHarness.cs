using System.Diagnostics;
using System.Text;

namespace TessynDesktop.WPF.E2E.Tests;

/// <summary>Starts the built WPF app against a temp data root and daemon file (<c>--test-window</c>), reads its log and stops it.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class AppProcessHarness
{
	private readonly string _root;
	private readonly string _daemonFile;

	public AppProcessHarness(string root, string daemonFile)
	{
		_root = root;
		_daemonFile = daemonFile;
	}

	public Process Start(params string[] extraArguments)
	{
		var exe = new BuiltAppLocator().FindExe();
		var info = new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe)! };
		info.ArgumentList.Add("--test-window");
		info.ArgumentList.Add("--data-root");
		info.ArgumentList.Add(_root);
		info.ArgumentList.Add("--daemon-file");
		info.ArgumentList.Add(_daemonFile);
		foreach (var argument in extraArguments)
			info.ArgumentList.Add(argument);
		return Process.Start(info)!;
	}

	/// <summary>Waits until the app's log contains every needle; false on timeout or when the app exited.</summary>
	public async Task<bool> WaitForLogAsync(Process process, TimeSpan timeout, params string[] needles)
	{
		var deadline = DateTime.UtcNow + timeout;
		while (DateTime.UtcNow < deadline && !process.HasExited)
		{
			var text = ReadLog();
			if (needles.All(n => text.Contains(n, StringComparison.Ordinal)))
				return true;
			await Task.Delay(250);
		}

		return false;
	}

	/// <summary>Occurrences of <paramref name="needle"/> in the log.</summary>
	public int Count(string needle) => ReadLog().Split(needle).Length - 1;

	public string ReadLog()
	{
		var dir = Path.Combine(_root, "logs");
		if (!Directory.Exists(dir))
			return string.Empty;

		var text = new StringBuilder();
		foreach (var file in Directory.EnumerateFiles(dir, "wpf-*.txt").OrderBy(f => f, StringComparer.Ordinal))
		{
			using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
			using var reader = new StreamReader(stream);
			text.Append(reader.ReadToEnd());
		}

		return text.ToString();
	}

	/// <summary>Closes the window and waits for the process to end; kills it when it does not.</summary>
	public void Stop(Process process)
	{
		if (process.HasExited)
			return;

		new AppWindowFinder().Close(process);
		if (!process.WaitForExit(TimeSpan.FromSeconds(15)))
			process.Kill(entireProcessTree: true);
	}
}
