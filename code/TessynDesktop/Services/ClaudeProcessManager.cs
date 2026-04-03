using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
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
		CancellationToken cancellationToken = default)
	{
		var args = BuildArguments(sessionId, model);
		_log.Debug("Attempting to spawn claude. Path={ClaudePath} Args={Args} WorkDir={WorkDir} ConfigDir={ConfigDir}",
			claudePath, args, workingDirectory, profileConfigDir);

		Process? process = TryStartProcess(claudePath, args, workingDirectory, profileConfigDir);

		// On Windows, 'claude' is often a .cmd file which requires cmd.exe to launch
		// when UseShellExecute=false. Retry via cmd.exe /c if direct spawn failed.
		if (process == null && OperatingSystem.IsWindows())
		{
			var cmdArgs = $"/c \"{claudePath}\" {args}";
			_log.Debug("Direct spawn failed — retrying via cmd.exe /c. Args={CmdArgs}", cmdArgs);
			process = TryStartProcess("cmd.exe", cmdArgs, workingDirectory, profileConfigDir);
		}

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
				_log.Debug("Writing user message to stdin ({Length} chars)", userMessage.Length);
				await process.StandardInput.WriteLineAsync(userMessage);
				process.StandardInput.Close();

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

				await process.WaitForExitAsync(cancellationToken);
				_log.Debug("Claude process exited. Code={ExitCode}", process.ExitCode);

				var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
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
		int timeoutMs = 60000,
		CancellationToken cancellationToken = default)
	{
		var args = BuildPrintModeArguments(model);
		_log.Debug("RunPrintModeAsync: spawning claude. Path={ClaudePath} Args={Args}", claudePath, args);

		Process? process = TryStartProcess(claudePath, args, Directory.GetCurrentDirectory());

		// Windows .cmd retry
		if (process == null && OperatingSystem.IsWindows())
		{
			var cmdArgs = $"/c \"{claudePath}\" {args}";
			_log.Debug("RunPrintModeAsync: retrying via cmd.exe /c. Args={CmdArgs}", cmdArgs);
			process = TryStartProcess("cmd.exe", cmdArgs, Directory.GetCurrentDirectory());
		}

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
				await process.StandardInput.WriteLineAsync(prompt);
				process.StandardInput.Close();

				using var timeoutCts = new CancellationTokenSource(timeoutMs);
				using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
					cancellationToken, timeoutCts.Token);

				var stdout = await process.StandardOutput.ReadToEndAsync(linkedCts.Token);
				await process.WaitForExitAsync(linkedCts.Token);

				var stderr = await process.StandardError.ReadToEndAsync(linkedCts.Token);
				if (!string.IsNullOrWhiteSpace(stderr))
					_log.Warning("RunPrintModeAsync: stderr: {Stderr}", stderr.Trim());

				if (process.ExitCode != 0)
				{
					_log.Warning("RunPrintModeAsync: exit code {ExitCode}. stderr={Stderr}",
						process.ExitCode, stderr.Trim());
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

	private static string BuildArguments(string? sessionId, string? model = null)
	{
		// -p (--print) forces non-interactive single-prompt mode.
		// --verbose is required by claude when combining --print with stream-json output.
		// --dangerously-skip-permissions suppresses all permission prompts.
		var args = "--output-format stream-json --verbose --dangerously-skip-permissions -p";
		if (!string.IsNullOrEmpty(sessionId))
			args += $" --resume {sessionId}";
		if (!string.IsNullOrEmpty(model))
			args += $" --model {model}";
		return args;
	}

	private static Process? TryStartProcess(string fileName, string arguments, string workingDirectory, string? profileConfigDir = null)
	{
		var psi = new ProcessStartInfo(fileName, arguments)
		{
			WorkingDirectory       = workingDirectory,
			RedirectStandardInput  = true,
			RedirectStandardOutput = true,
			RedirectStandardError  = true,
			UseShellExecute        = false,
			CreateNoWindow         = true,
			StandardInputEncoding  = Encoding.UTF8,
			StandardOutputEncoding = Encoding.UTF8,
			StandardErrorEncoding  = Encoding.UTF8,
		};

		// Remove CLAUDECODE so claude doesn't refuse to run inside another claude session.
		psi.Environment.Remove("CLAUDECODE");

		// Set CLAUDE_CONFIG_DIR to isolate auth context for non-default profiles.
		if (!string.IsNullOrEmpty(profileConfigDir))
			psi.Environment["CLAUDE_CONFIG_DIR"] = profileConfigDir;

		try
		{
			return Process.Start(psi);
		}
		catch (Win32Exception ex)
		{
			_log.Warning("Win32Exception starting {FileName}: {Message}", fileName, ex.Message);
			return null;
		}
	}

	private static ClaudeStreamEvent? TryParseEvent(string line)
	{
		try
		{
			using var doc = JsonDocument.Parse(line);
			var root = doc.RootElement;

			if (!root.TryGetProperty("type", out var typeEl))
				return null;

			var type    = typeEl.GetString() ?? string.Empty;
			var subtype = root.TryGetProperty("subtype", out var subEl) ? subEl.GetString() : null;

			return type switch
			{
				"assistant" => ParseAssistantEvent(root, type, subtype),
				"system"    => ParseSystemEvent(root, type, subtype),
				"result"    => ParseResultEvent(root, type, subtype),
				_           => null,
			};
		}
		catch (JsonException)
		{
			return null;
		}
	}

	private static ClaudeStreamEvent? ParseAssistantEvent(JsonElement root, string type, string? subtype)
	{
		if (!root.TryGetProperty("message", out var msg))
			return null;

		var content = ExtractTextContent(msg);
		if (string.IsNullOrEmpty(content))
			return null;

		return new ClaudeStreamEvent { Type = type, Subtype = subtype, Content = content };
	}

	private static ClaudeStreamEvent ParseSystemEvent(JsonElement root, string type, string? subtype)
	{
		string? content = null;

		if (root.TryGetProperty("summary", out var summary))
			content = summary.GetString();
		else if (root.TryGetProperty("message", out var msg))
			content = msg.GetString();
		else if (subtype is "task_progress" or "task_started")
		{
			// Show live tool-use descriptions as progress feedback
			var description = root.TryGetProperty("description", out var desc) ? desc.GetString() : null;
			var tool        = root.TryGetProperty("last_tool_name", out var tn)  ? tn.GetString()   : null;
			content = tool != null ? $"[{tool}] {description}" : description;
		}

		return new ClaudeStreamEvent { Type = type, Subtype = subtype, Content = content };
	}

	private static ClaudeStreamEvent ParseResultEvent(JsonElement root, string type, string? subtype)
	{
		var sessionId = root.TryGetProperty("session_id", out var sidEl) ? sidEl.GetString() : null;
		var isError   = root.TryGetProperty("is_error", out var errEl) && errEl.GetBoolean();

		string? errorMsg = null;
		if (isError)
		{
			// Claude emits errors as an "errors" array of strings, not a single "error" property.
			if (root.TryGetProperty("errors", out var errorsArr) && errorsArr.ValueKind == JsonValueKind.Array)
			{
				var sb = new StringBuilder();
				foreach (var item in errorsArr.EnumerateArray())
				{
					if (sb.Length > 0)
						sb.Append("; ");
					sb.Append(item.GetString());
				}
				if (sb.Length > 0)
					errorMsg = sb.ToString();
			}
			// Fallback: also check singular "error" property just in case
			else if (root.TryGetProperty("error", out var errMsgEl))
				errorMsg = errMsgEl.GetString();
		}

		return new ClaudeStreamEvent
		{
			Type      = type,
			Subtype   = subtype,
			SessionId = sessionId,
			IsError   = isError,
			Content   = errorMsg,
		};
	}

	private static string? ExtractTextContent(JsonElement messageElement)
	{
		if (!messageElement.TryGetProperty("content", out var contentArray))
			return null;

		var sb = new StringBuilder();
		foreach (var block in contentArray.EnumerateArray())
		{
			if (!block.TryGetProperty("type", out var blockType))
				continue;

			if (blockType.GetString() == "text" && block.TryGetProperty("text", out var text))
				sb.Append(text.GetString());
		}

		return sb.Length > 0 ? sb.ToString().TrimEnd() : null;
	}
}
