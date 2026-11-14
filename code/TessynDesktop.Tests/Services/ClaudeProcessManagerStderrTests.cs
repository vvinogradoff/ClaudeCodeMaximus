using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary>
/// Robustness item 0.6/3: the one-shot process path used to read stderr only after stdout ended, so a child that
/// fills the stderr pipe before finishing stdout blocked forever (neither side can progress). Uses a real child
/// process (a .cmd wrapper around a PowerShell script standing in for claude), so Windows only.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class ClaudeProcessManagerStderrTests : IDisposable
{
	private const int StderrBytes = 400_000; // far beyond any pipe buffer

	private readonly string _dir;

	public ClaudeProcessManagerStderrTests()
	{
		_dir = Path.Combine(Path.GetTempPath(), "cmx_stderr_test_" + Path.GetRandomFileName());
		Directory.CreateDirectory(_dir);
	}

	public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ } }

	private string WriteFakeClaude(int exitCode)
	{
		File.WriteAllText(Path.Combine(_dir, "fake.ps1"),
			$"[void][Console]::In.ReadLine()\r\n" +
			$"[Console]::Error.Write(('e' * {StderrBytes}))\r\n" +
			"[Console]::Out.WriteLine('{\"type\":\"result\",\"session_id\":\"s1\",\"result\":\"done\"}')\r\n" +
			$"exit {exitCode}\r\n");
		var cmd = Path.Combine(_dir, "fake-claude.cmd");
		File.WriteAllText(cmd, "@powershell -NoProfile -ExecutionPolicy Bypass -File \"%~dp0fake.ps1\"\r\n");
		return cmd;
	}

	private ClaudeProcessManager BuildManager()
	{
		var settings = new AppSettingsService(_dir);
		return new ClaudeProcessManager(settings, new ClaudeProcessLauncher(settings));
	}

	[Fact]
	public async Task SendMessageAsync_ChildFloodsStderrBeforeStdoutEnds_DoesNotDeadlock()
	{
		if (!OperatingSystem.IsWindows())
			return;

		var events = new System.Collections.Concurrent.ConcurrentQueue<ClaudeStreamEvent>();
		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45)); // cleanup only; the assertion is below
		var run = BuildManager().SendMessageAsync(_dir, WriteFakeClaude(exitCode: 1), null, "hi", events.Enqueue,
			cancellationToken: cts.Token);

		var finished = await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(30))) == run;
		if (!finished)
			cts.Cancel(); // kill the stuck child so the test host does not leak it
		Assert.True(finished, "SendMessageAsync deadlocked: stderr pipe full while stdout was still being read");
		await run;

		Assert.Contains(events, e => e.IsError && e.Content!.StartsWith("claude exited with code 1"));
	}

	[Fact]
	public async Task RunPrintModeAsync_ChildFloodsStderr_DoesNotDeadlock()
	{
		if (!OperatingSystem.IsWindows())
			return;

		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
		var run = BuildManager().RunPrintModeAsync(WriteFakeClaude(exitCode: 0), "hi", timeoutMs: 40_000,
			cancellationToken: cts.Token);

		var finished = await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(30))) == run;
		if (!finished)
			cts.Cancel();
		Assert.True(finished, "RunPrintModeAsync deadlocked: stderr pipe full while stdout was still being read");
		Assert.Contains("done", (await run) ?? "");
	}
}
