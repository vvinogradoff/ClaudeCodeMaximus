using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Models;
using Serilog;

namespace TessynDesktop.Services;

/// <remarks>Created by Claude</remarks>
public sealed class ClaudeProcessManager : IClaudeProcessManager
{
	private static readonly ILogger _log = Log.ForContext<ClaudeProcessManager>();
	private readonly ConcurrentDictionary<int, Process> _activeProcesses = new();
	private readonly IAppSettingsService _appSettings;
	private readonly ClaudeProcessLauncher _launcher;

	public ClaudeProcessManager(IAppSettingsService appSettings, ClaudeProcessLauncher launcher)
	{
		_appSettings = appSettings;
		_launcher    = launcher;
	}

	public int ActiveProcessCount => _activeProcesses.Count;

	public void TerminateAll()
	{
		foreach (var (pid, proc) in _activeProcesses)
		{
			_log.Information("Terminating claude process PID={Pid}", pid);
			try   { proc.Kill(entireProcessTree: true); }
			catch (Exception ex) { _log.Warning(ex, "Failed to kill PID={Pid}", pid); }
		}
	}

	public async Task SendMessageAsync(
		string workingDirectory,
		string claudePath,
		string? sessionId,
		string userMessage,
		Action<ClaudeStreamEvent> onEvent,
		string? model = null,
		string? profileConfigDir = null,
		string? effort = null,
		string? mcpConfigPath = null,
		string? ollamaBaseUrl = null,
		bool disableTools = false,
		bool chrome = false,
		bool disallowNativeScheduling = false,
		LocalToolSpec? localToolSpec = null,
		bool strictMcpConfig = false,
		CancellationToken cancellationToken = default)
	{
		var args = BuildArguments(sessionId, model, effort, mcpConfigPath, disableTools, chrome, disallowNativeScheduling,
			localToolSpec, strictMcpConfig);
		_log.Debug("Attempting to spawn claude. Path={ClaudePath} Args={Args} WorkDir={WorkDir} ConfigDir={ConfigDir} OllamaBaseUrl={OllamaBaseUrl}",
			claudePath, args, workingDirectory, profileConfigDir, ollamaBaseUrl);

		Process? process = _launcher.Start(claudePath, args, workingDirectory, profileConfigDir, ollamaBaseUrl);

		if (process == null)
		{
			_log.Error("Failed to start claude process. Path={ClaudePath}", claudePath);
			onEvent(new ClaudeStreamEvent
			{
				Type    = "system",
				Subtype = "error",
				Content = $"Could not launch claude at '{claudePath}'. Check the claude path in Settings.",
				IsError = true,
			});
			return;
		}

		_log.Debug("Claude process started. PID={Pid}", process.Id);
		_activeProcesses.TryAdd(process.Id, process);

		using (process)
		{
			try
			{
				// Drain stderr from the start: if the child fills the stderr pipe while still writing stdout, reading
				// stderr only after stdout ends would block both sides forever. Killing the process (cancellation) closes
				// the pipe and ends the read.
				var stderrTask = process.StandardError.ReadToEndAsync(CancellationToken.None);
				_ = stderrTask.ContinueWith(static t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted); // never leave it unobserved

				_log.Debug("Writing user message to stdin ({Length} chars)", userMessage.Length);
				await process.StandardInput.WriteLineAsync(userMessage);
				process.StandardInput.Close();

				// On Windows, StreamReader.ReadLineAsync(CancellationToken) may not unblock when the
				// token is cancelled while a blocking pipe-read is in progress. Register a callback
				// that kills the process immediately so the pipe closes and the read returns.
				using var killOnCancel = cancellationToken.Register(() =>
				{
					_log.Information("Cancellation requested - killing claude process PID={Pid}", process.Id);
					try { process.Kill(entireProcessTree: true); }
					catch (Exception ex) { _log.Warning(ex, "Failed to kill process on cancellation PID={Pid}", process.Id); }
				});

				string? line;
				while ((line = await process.StandardOutput.ReadLineAsync(cancellationToken)) != null)
				{
					if (string.IsNullOrWhiteSpace(line))
						continue;

					_log.Debug("stdout: {Line}", line);

					var evt = TryParseEvent(line);
					if (evt != null)
						onEvent(evt);
				}

				// If the loop exited because the process was killed on cancellation, propagate.
				cancellationToken.ThrowIfCancellationRequested();

				await process.WaitForExitAsync(cancellationToken);
				_log.Debug("Claude process exited. Code={ExitCode}", process.ExitCode);

				var stderr = await stderrTask;
				if (!string.IsNullOrWhiteSpace(stderr))
				{
					_log.Warning("stderr: {Stderr}", stderr.Trim());
					if (process.ExitCode != 0)
					{
						onEvent(new ClaudeStreamEvent
						{
							Type    = "system",
							Subtype = "error",
							Content = $"claude exited with code {process.ExitCode}: {stderr.Trim()}",
							IsError = true,
						});
					}
				}
			}
			catch (OperationCanceledException)
			{
				// Process may already be dead (killed by killOnCancel above), kill is best-effort.
				try { process.Kill(entireProcessTree: true); }
				catch { /* already dead */ }
				throw;
			}
			finally
			{
				_activeProcesses.TryRemove(process.Id, out _);
			}
		}
	}

	public async Task<string?> RunPrintModeAsync(
		string claudePath,
		string prompt,
		string? model = null,
		string? profileConfigDir = null,
		int timeoutMs = 60000,
		CancellationToken cancellationToken = default)
	{
		var args = BuildPrintModeArguments(model);
		_log.Debug("RunPrintModeAsync: spawning claude. Path={ClaudePath} Args={Args} ProfileConfigDir={ProfileConfigDir}",
			claudePath, args, profileConfigDir);

		Process? process = _launcher.Start(claudePath, args, Directory.GetCurrentDirectory(), profileConfigDir);

		if (process == null)
		{
			_log.Error("RunPrintModeAsync: failed to start claude. Path={ClaudePath}", claudePath);
			return null;
		}

		_log.Debug("RunPrintModeAsync: process started PID={Pid}", process.Id);
		_activeProcesses.TryAdd(process.Id, process);

		using (process)
		{
			try
			{
				// Drain stderr concurrently with stdout (see SendMessageAsync): a full stderr pipe must not stall stdout.
				var stderrTask = process.StandardError.ReadToEndAsync(CancellationToken.None);
				_ = stderrTask.ContinueWith(static t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted); // never leave it unobserved

				await process.StandardInput.WriteLineAsync(prompt);
				process.StandardInput.Close();

				using var timeoutCts = new CancellationTokenSource(timeoutMs);
				using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
					cancellationToken, timeoutCts.Token);

				var stdout = await process.StandardOutput.ReadToEndAsync(linkedCts.Token);
				await process.WaitForExitAsync(linkedCts.Token);

				var stderr = await stderrTask.WaitAsync(linkedCts.Token);
				if (!string.IsNullOrWhiteSpace(stderr))
					_log.Warning("RunPrintModeAsync: stderr: {Stderr}", stderr.Trim());

				if (process.ExitCode != 0)
				{
					var stdoutSnippet = stdout.Length > 500 ? stdout[..500] + "…" : stdout;
					_log.Warning("RunPrintModeAsync: exit code {ExitCode}. stderr={Stderr}. stdout={Stdout}",
						process.ExitCode, stderr.Trim(), stdoutSnippet.Trim());
					return null;
				}

				return stdout;
			}
			catch (OperationCanceledException)
			{
				_log.Warning("RunPrintModeAsync: timed out or cancelled");
				try { process.Kill(entireProcessTree: true); }
				catch { /* best effort */ }
				return null;
			}
			finally
			{
				_activeProcesses.TryRemove(process.Id, out _);
			}
		}
	}

	private static string BuildPrintModeArguments(string? model)
	{
		// -p for print mode, --tools "" to disable tools, --no-session-persistence to avoid creating sessions,
		// --output-format json for structured output, --dangerously-skip-permissions for headless operation.
		var args = "-p --tools \"\" --no-session-persistence --output-format json --dangerously-skip-permissions";
		if (!string.IsNullOrEmpty(model))
			args += $" --model {model}";
		return args;
	}

	/// <summary>Internal (not private) so <c>TessynDesktop.Tests</c> can assert on assembled CLI args directly (FR.12.21 test seam — see AssemblyInfo.cs).</summary>
	internal static string BuildArguments(
		string? sessionId,
		string? model = null,
		string? effort = null,
		string? mcpConfigPath = null,
		bool disableTools = false,
		bool chrome = false,
		bool disallowNativeScheduling = false,
		LocalToolSpec? localToolSpec = null,
		bool strictMcpConfig = false)
	{
		// -p (--print) forces non-interactive single-prompt mode.
		// --verbose is required by claude when combining --print with stream-json output.
		// --dangerously-skip-permissions suppresses all permission prompts.
		var args = "--output-format stream-json --verbose --dangerously-skip-permissions -p";
		if (!string.IsNullOrEmpty(sessionId))
			args += $" --resume {sessionId}";
		if (!string.IsNullOrEmpty(model))
			args += $" --model {model}";
		if (!string.IsNullOrEmpty(effort))
			args += $" --effort {effort}";
		if (!string.IsNullOrEmpty(mcpConfigPath))
			args += $" --mcp-config \"{mcpConfigPath}\"";
		if (strictMcpConfig)
			args += " --strict-mcp-config";
		if (disableTools)
			args += " --tools \"\"";
		else if (localToolSpec != null)
			args += $" --tools \"{string.Join(' ', localToolSpec.NativeToolNames)}\"";
		// Experimental browser control (FR pending): asks the CLI to connect to the
		// Claude-in-Chrome extension. Interaction-centric feature; behaviour under -p is
		// unverified — the toggle exists so the flag can be exercised and observed.
		if (chrome)
			args += " --chrome";
		// FR.19.10 — hard-block the native scheduling tools on One-shot Mode spawns instead of
		// only advising against them via the instruction block (FR.14.11).
		if (disallowNativeScheduling)
			args += " --disallowed-tools " + string.Join(' ', Constants.Instructions.NativeSchedulingToolNames);
		return args;
	}

	private static ClaudeStreamEvent? TryParseEvent(string line) => ClaudeStreamEventParser.TryParseEvent(line);
}
