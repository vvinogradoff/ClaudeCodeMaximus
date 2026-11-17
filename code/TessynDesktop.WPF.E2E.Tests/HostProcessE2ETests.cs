using System.Diagnostics;
using TessynDesktop.Client;
using TessynDesktop.Services;

namespace TessynDesktop.WPF.E2E.Tests;

/// <summary>
/// End to end: the BUILT daemon host exe (<c>TessynDesktop.exe --background</c>) started in isolation: its own data root, its own <c>daemon.json</c> and its
/// own guard and exit-event names (<c>--data-root</c>, <c>--daemon-file</c>, <c>--instance</c>), so it never touches the real %AppData% or meets a real host.
/// It must come up (publish <c>daemon.json</c>), answer a TDCP read method, end cleanly when the exit event is signalled and remove <c>daemon.json</c>.
/// This is the check that the host starts at all (a crash before the services start leaves no <c>daemon.json</c>) and the automation of TC.HS.15.
/// </summary>
/// <remarks>Created by Claude</remarks>
[Collection("wpf-app-e2e")]
public sealed class HostProcessE2ETests : IDisposable
{
	private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(60);
	private static readonly TimeSpan ExitTimeout = TimeSpan.FromSeconds(60);

	private readonly string _root = Path.Combine(Path.GetTempPath(), "tessyn-host-e2e-" + Guid.NewGuid().ToString("N"));
	private readonly string _daemonFile;
	private readonly string _instance = "e2e" + Guid.NewGuid().ToString("N")[..8];
	private Process? _host;

	public HostProcessE2ETests()
	{
		Directory.CreateDirectory(_root);
		_daemonFile = Path.Combine(_root, "daemon.json");
	}

	public void Dispose()
	{
		if (_host is { HasExited: false })
			_host.Kill(entireProcessTree: true);
		_host?.Dispose();
		try
		{
			Directory.Delete(_root, recursive: true);
		}
		catch (IOException)
		{
		}
		catch (UnauthorizedAccessException)
		{
		}
	}

	private Process StartHost(params string[] arguments)
	{
		var exe = FindHostExe();
		var info = new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe)!, RedirectStandardError = true };
		foreach (var argument in arguments)
			info.ArgumentList.Add(argument);
		info.ArgumentList.Add("--data-root");
		info.ArgumentList.Add(_root);
		info.ArgumentList.Add("--daemon-file");
		info.ArgumentList.Add(_daemonFile);
		info.ArgumentList.Add("--instance");
		info.ArgumentList.Add(_instance);
		_host = Process.Start(info)!;
		return _host;
	}

	private async Task WaitForDaemonFileAsync(Process host)
	{
		var deadline = DateTime.UtcNow + StartTimeout;
		while (DateTime.UtcNow < deadline && !File.Exists(_daemonFile))
		{
			if (host.HasExited)
				Assert.Fail($"The host ended with exit code {host.ExitCode} before it published daemon.json. Log:\n{ReadLog()}");
			await Task.Delay(200);
		}

		Assert.True(File.Exists(_daemonFile), $"The host did not publish daemon.json within {StartTimeout.TotalSeconds:0} s. Log:\n{ReadLog()}");
	}

	private string ReadLog()
	{
		var dir = Path.Combine(_root, "logs");
		if (!Directory.Exists(dir))
			return "(no log directory)";

		var text = new System.Text.StringBuilder();
		foreach (var file in Directory.EnumerateFiles(dir))
		{
			using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
			using var reader = new StreamReader(stream);
			text.Append(reader.ReadToEnd());
		}

		return text.ToString();
	}

	[Fact]
	public async Task The_host_starts_publishes_daemon_json_answers_tree_snapshot_and_exits_cleanly_on_the_exit_event()
	{
		var host = StartHost("--background");
		await WaitForDaemonFileAsync(host);

		var discovered = await new DaemonDiscovery(_daemonFile).DiscoverAsync();
		Assert.Equal(DaemonDiscoveryStatus.Found, discovered.Status);
		Assert.Equal(host.Id, discovered.Daemon!.Pid);
		await using (var client = new TessynClient(discovered.Daemon!))
		{
			await client.ConnectAsync();
			var snapshot = await client.TreeSnapshotAsync();
			Assert.NotNull(snapshot);
		}

		// The host keeps all its data under the temp root: settings and logs are there, nothing went to the real data root.
		Assert.True(File.Exists(Path.Combine(_root, "appsettings.json")));
		Assert.Contains("Running as the background daemon host", ReadLog());
		Assert.DoesNotContain("Started without --background", ReadLog());

		var signalled = DaemonHostExitListener.Signal(BackgroundHostOptions.WithInstance(TessynDesktop.Models.Remote.RemoteProtocol.Discovery.HostExitEventName, _instance));
		Assert.True(signalled, "No host listens on the exit event");
		Assert.True(host.WaitForExit(ExitTimeout), "The host did not end after the exit event. Log:\n" + ReadLog());
		Assert.Equal(0, host.ExitCode);
		Assert.False(File.Exists(_daemonFile), "daemon.json was not removed on a clean exit");
	}

	[Fact]
	public async Task A_start_without_background_runs_as_the_host_and_logs_a_note_and_a_second_start_ends_at_once()
	{
		var host = StartHost();
		await WaitForDaemonFileAsync(host);
		Assert.Contains("Started without --background", ReadLog());

		// The same instance name means the same guard: a second host leaves the first alone.
		var second = StartHost("--background");
		Assert.True(second.WaitForExit(TimeSpan.FromSeconds(30)), "The second host did not end at the guard");
		Assert.Equal(0, second.ExitCode);
		Assert.False(host.HasExited);

		DaemonHostExitListener.Signal(BackgroundHostOptions.WithInstance(TessynDesktop.Models.Remote.RemoteProtocol.Discovery.HostExitEventName, _instance));
		Assert.True(host.WaitForExit(ExitTimeout));
	}

	private static string FindHostExe()
	{
		for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
		{
			var bin = Path.Combine(dir.FullName, "TessynDesktop", "bin");
			if (!Directory.Exists(bin))
				continue;

			var newest = Directory.EnumerateFiles(bin, "TessynDesktop.exe", SearchOption.AllDirectories)
				.Where(f => File.Exists(Path.ChangeExtension(f, ".dll")))
				.OrderByDescending(File.GetLastWriteTimeUtc)
				.FirstOrDefault();
			if (newest != null)
				return newest;
		}

		throw new FileNotFoundException("The built TessynDesktop.exe (the daemon host) was not found; build code/TessynDesktop.sln first.");
	}
}
