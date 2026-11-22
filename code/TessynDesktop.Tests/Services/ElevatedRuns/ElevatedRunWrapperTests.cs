using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services.ElevatedRuns;

/// <summary>
/// TC.ER.32 to TC.ER.37: the real wrapper script (FR.15.19) run <b>without elevation</b> through <c>powershell.exe -EncodedCommand</c>, against the real <c>cmd.exe</c>,
/// with harmless commands (echo, ping, exit). Nothing here asks Windows for administrator rights.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class ElevatedRunWrapperTests : IDisposable
{
	private readonly string _root = Path.Combine(Path.GetTempPath(), "er_wrapper_" + Guid.NewGuid().ToString("N")[..12]);
	private readonly ElevatedRunLaunchBuilder _builder = new();

	public ElevatedRunWrapperTests() => Directory.CreateDirectory(_root);

	public void Dispose()
	{
		try { Directory.Delete(_root, recursive: true); }
		catch (IOException) { }
	}

	/// <summary>A started wrapper: its process, its run folder and what it printed itself (for the failure message).</summary>
	private sealed class Running(Process process, string runDir, StringBuilder printed)
	{
		public Process Process { get; } = process;
		public string RunDir { get; } = runDir;
		public string File(string name) => Path.Combine(RunDir, name);
		public string Printed => printed.ToString();
	}

	private Running Start(string command, string? workingDir = null, int seconds = 600)
	{
		var runDir = Path.Combine(_root, Guid.NewGuid().ToString("N")[..8]);
		Directory.CreateDirectory(runDir);
		var script  = _builder.BuildScript(command, workingDir ?? _root, runDir, seconds);
		var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));

		var info = new ProcessStartInfo("powershell.exe", Constants.ElevatedRuns.PowerShellArgumentsPrefix + encoded)
		{
			UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = _root,
		};
		var printed = new StringBuilder();
		var process = new Process { StartInfo = info };
		process.OutputDataReceived += (_, e) => { lock (printed) printed.AppendLine(e.Data); };
		process.ErrorDataReceived += (_, e) => { lock (printed) printed.AppendLine(e.Data); };
		process.Start();
		process.BeginOutputReadLine();
		process.BeginErrorReadLine();
		return new Running(process, runDir, printed);
	}

	private static void WaitForExit(Running run, int seconds = 90)
	{
		if (!run.Process.WaitForExit(seconds * 1000))
		{
			run.Process.Kill(entireProcessTree: true);
			Assert.Fail("The wrapper did not end within " + seconds + " s. It printed: " + run.Printed);
		}

		run.Process.WaitForExit(); // the redirected streams are drained
	}

	private static async Task<T> WaitForAsync<T>(Func<T?> probe, int seconds = 60) where T : class
	{
		var until = DateTime.UtcNow.AddSeconds(seconds);
		while (DateTime.UtcNow < until)
		{
			if (probe() is { } found)
				return found;
			await Task.Delay(50);
		}

		throw new TimeoutException("The condition was not met within " + seconds + " s.");
	}

	private static JsonNode ReadJson(string path)
	{
		// The wrapper writes atomically (temp file, then rename), so a file that exists is whole.
		return JsonNode.Parse(File.ReadAllText(path).TrimStart('﻿'))!;
	}

	private Running RunToEnd(string command, string? workingDir = null, int seconds = 600)
	{
		var run = Start(command, workingDir, seconds);
		WaitForExit(run);
		Assert.True(File.Exists(run.File("result.json")), "No result.json. The wrapper printed: " + run.Printed);
		return run;
	}

	private static string[] Lines(Running run) =>
		File.ReadAllText(run.File("output.log")).ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);

	// ── TC.ER.32 ──────────────────────────────────────────────────────────────

	[Fact]
	public void ExitCode_IsReported_BothStreamsAreLogged_AndTheResultIsWrittenLast_TC_ER_32()
	{
		var run = RunToEnd("echo out & echo err 1>&2 & exit 7");

		var result = ReadJson(run.File("result.json"));
		Assert.Equal(7, result["exitCode"]!.GetValue<int>());
		Assert.Equal("exited", result["ended"]!.GetValue<string>());
		Assert.NotNull(result["endedUtc"]!.GetValue<DateTimeOffset>());

		var lines = Lines(run).Select(l => l.Trim()).ToList();
		Assert.Contains("out", lines);
		Assert.Contains("err", lines);

		var resultWritten = File.GetLastWriteTimeUtc(run.File("result.json"));
		Assert.True(resultWritten >= File.GetLastWriteTimeUtc(run.File("started.json")));
		Assert.True(resultWritten >= File.GetLastWriteTimeUtc(run.File("output.log")));
		Assert.Empty(Directory.GetFiles(run.RunDir, "*.tmp"));
	}

	[Fact]
	public async Task StartedJson_HasALivePid_WhileTheCommandRuns_TC_ER_32()
	{
		var run = Start("ping -n 6 127.0.0.1 >nul");

		var started = await WaitForAsync(() => File.Exists(run.File("started.json")) ? ReadJson(run.File("started.json")) : null);
		var pid     = started["pid"]!.GetValue<int>();

		Assert.True(pid > 0);
		using (var wrapper = Process.GetProcessById(pid))
			Assert.False(wrapper.HasExited);
		Assert.True(started["commandPid"]!.GetValue<int>() > 0);
		Assert.NotNull(started["startedUtc"]!.GetValue<DateTimeOffset>());
		Assert.False(File.Exists(run.File("result.json")));

		WaitForExit(run);
		Assert.True(File.Exists(run.File("result.json")));
	}

	// ── TC.ER.33 ──────────────────────────────────────────────────────────────

	[Theory]
	[InlineData("echo \"quo ted\" & echo second", "\"quo ted\"", "second")]
	[InlineData("echo caret ^^ amp ^& end", "caret ^ amp & end", null)]
	[InlineData("echo café üñï", "café üñï", null)]
	[InlineData("powershell -NoProfile -Command \"Write-Output ('a'+'b')\"", "ab", null)]
	[InlineData("echo \"it's\" \"50%%\"", "\"it's\" \"50%%\"", null)]
	public void Verbatim_TheCommandRunsExactlyAsGiven_TC_ER_33(string command, string first, string? second)
	{
		var run = RunToEnd(command);

		var lines = Lines(run).Select(l => l.Trim()).ToList();
		Assert.Equal(first, lines[0]);
		if (second != null)
			Assert.Equal(second, lines[1]);
		Assert.Equal(0, ReadJson(run.File("result.json"))["exitCode"]!.GetValue<int>());
	}

	[Fact]
	public void Verbatim_AnEnvironmentVariableIsExpandedByCmdAsAnyCommandWouldBe_TC_ER_33()
	{
		var run = RunToEnd("echo %COMPUTERNAME%");

		Assert.Equal(Environment.MachineName, Lines(run)[0].Trim(), ignoreCase: true);
	}

	[Fact]
	public void Verbatim_AVeryLongArgumentSurvives_AndNoScriptIsLeftInTheRunFolder_TC_ER_33()
	{
		var argument = new string('x', 3500);

		var run = RunToEnd("echo " + argument);

		Assert.Equal(argument, Lines(run)[0].Trim());
		var names = Directory.GetFiles(run.RunDir).Select(Path.GetFileName).Order().ToList();
		Assert.Equal(["output.log", "result.json", "started.json"], names);
	}

	// ── TC.ER.34 ──────────────────────────────────────────────────────────────

	[Fact]
	public void WorkingDirectory_IsTheStoredFolder_AlsoWithSpaces_TC_ER_34()
	{
		var withSpaces = Path.Combine(_root, "a folder with spaces");
		Directory.CreateDirectory(withSpaces);

		var plain  = RunToEnd("cd");
		var spaced = RunToEnd("cd", withSpaces);

		Assert.Equal(_root, Lines(plain)[0].Trim(), ignoreCase: true);
		Assert.Equal(withSpaces, Lines(spaced)[0].Trim(), ignoreCase: true);
	}

	// ── TC.ER.35, 36 ──────────────────────────────────────────────────────────

	[Fact]
	public async Task Stop_TheStopFileKillsTheWholeTreeWithinSeconds_TC_ER_35()
	{
		var run = Start("cmd /c ping -n 120 127.0.0.1");
		var started = await WaitForAsync(() => File.Exists(run.File("started.json")) ? ReadJson(run.File("started.json")) : null);
		var commandPid = started["commandPid"]!.GetValue<int>();
		await Task.Delay(2000);

		var stopAt = Stopwatch.StartNew();
		File.WriteAllText(run.File("stop"), string.Empty);
		WaitForExit(run, seconds: 30);
		stopAt.Stop();

		Assert.True(stopAt.Elapsed < TimeSpan.FromSeconds(8), stopAt.Elapsed.ToString());
		Assert.Equal("stopped", ReadJson(run.File("result.json"))["ended"]!.GetValue<string>());
		Assert.Throws<ArgumentException>(() => Process.GetProcessById(commandPid));
	}

	[Fact]
	public void TimeLimit_TheTreeIsKilledAtTheLimit_TC_ER_36()
	{
		var clock = Stopwatch.StartNew();

		var run = RunToEnd("cmd /c ping -n 120 127.0.0.1", seconds: 3);

		Assert.Equal("timedOut", ReadJson(run.File("result.json"))["ended"]!.GetValue<string>());
		Assert.True(clock.Elapsed < TimeSpan.FromSeconds(60), clock.Elapsed.ToString());
	}

	// ── TC.ER.37 ──────────────────────────────────────────────────────────────

	[Fact]
	public void OutputCap_TheLogStopsAt20MB_WithOneMarkerLine_AndTheProcessRunsToItsEnd_TC_ER_37()
	{
		var run = RunToEnd("powershell -NoProfile -Command \"$s='x'*1048576; 1..26|ForEach-Object{[Console]::Out.Write($s)}; exit 5\"");

		var length = new FileInfo(run.File("output.log")).Length;
		var text   = File.ReadAllText(run.File("output.log"));
		Assert.True(length <= Constants.ElevatedRuns.OutputCapBytes + 200, length.ToString());
		Assert.True(length >= Constants.ElevatedRuns.OutputCapBytes, length.ToString());
		Assert.EndsWith("[output truncated at 20 MB]" + Environment.NewLine, text);
		Assert.Single(System.Text.RegularExpressions.Regex.Matches(text, @"\[output truncated at 20 MB\]"));

		var result = ReadJson(run.File("result.json"));
		Assert.Equal(5, result["exitCode"]!.GetValue<int>());
		Assert.Equal("exited", result["ended"]!.GetValue<string>());
	}

	// ── the real files are the ones the daemon reads ──────────────────────────

	[Fact]
	public void TheFilesTheWrapperWritesAreReadByTheDaemonsModels_TC_ER_32()
	{
		var run = RunToEnd("exit /b 3");

		var started = JsonSerializer.Deserialize<ElevatedRunStartedFileModel>(File.ReadAllText(run.File("started.json")), TdcpJsonOptions.Default)!;
		var result  = JsonSerializer.Deserialize<ElevatedRunResultFileModel>(File.ReadAllText(run.File("result.json")), TdcpJsonOptions.Default)!;

		Assert.True(started.Pid > 0);
		Assert.Equal(3, result.ExitCode);
		Assert.Equal("exited", result.Ended);
		Assert.True(result.EndedUtc >= started.StartedUtc);
	}
}
