using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ClaudeMaximus.Models;
using Serilog;

namespace ClaudeMaximus.Services;

/// <remarks>Created by Claude</remarks>
public sealed class ClaudeProcessManager : IClaudeProcessManager
{
	private static readonly ILogger _log = Log.ForContext<ClaudeProcessManager>();
	private readonly ConcurrentDictionary<int, Process> _activeProcesses = new();
	private readonly IAppSettingsService _appSettings;

	public ClaudeProcessManager(IAppSettingsService appSettings)
	{
		_appSettings = appSettings;
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
		CancellationToken cancellationToken = default)
	{
		var args = BuildArguments(sessionId, model, effort, mcpConfigPath);
		_log.Debug("Attempting to spawn claude. Path={ClaudePath} Args={Args} WorkDir={WorkDir} ConfigDir={ConfigDir} OllamaBaseUrl={OllamaBaseUrl}",
			claudePath, args, workingDirectory, profileConfigDir, ollamaBaseUrl);

		Process? process = TryStartProcess(claudePath, args, workingDirectory, profileConfigDir, ollamaBaseUrl);

		// On Windows, 'claude' is often a .cmd file which requires cmd.exe to launch
		// when UseShellExecute=false. Retry via cmd.exe /c if direct spawn failed.
		if (process == null && OperatingSystem.IsWindows())
		{
			var cmdArgs = $"/c \"{claudePath}\" {args}";
			_log.Debug("Direct spawn failed — retrying via cmd.exe /c. Args={CmdArgs}", cmdArgs);
			process = TryStartProcess("cmd.exe", cmdArgs, workingDirectory, profileConfigDir, ollamaBaseUrl);
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

		Process? process = TryStartProcess(claudePath, args, Directory.GetCurrentDirectory(), profileConfigDir);

		// Windows .cmd retry
		if (process == null && OperatingSystem.IsWindows())
		{
			var cmdArgs = $"/c \"{claudePath}\" {args}";
			_log.Debug("RunPrintModeAsync: retrying via cmd.exe /c. Args={CmdArgs}", cmdArgs);
			process = TryStartProcess("cmd.exe", cmdArgs, Directory.GetCurrentDirectory(), profileConfigDir);
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

	private static string BuildArguments(
		string? sessionId,
		string? model = null,
		string? effort = null,
		string? mcpConfigPath = null)
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
		return args;
	}

	private Process? TryStartProcess(
		string fileName,
		string arguments,
		string workingDirectory,
		string? profileConfigDir = null,
		string? ollamaBaseUrl = null)
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

		if (!string.IsNullOrEmpty(ollamaBaseUrl))
		{
			// Local model routing (FR.12.14): redirect Claude SDK to the Ollama endpoint.
			// Profile auth and proxy are not used — billing is local hardware.
			// ANTHROPIC_BASE_URL must be the bare host without /v1 — the Anthropic SDK
			// appends /v1/messages internally, so including /v1 creates a double path.
			// ANTHROPIC_API_KEY must be non-empty; the SDK rejects an empty string before
			// sending the request, so we use the "ollama" placeholder for both.
			psi.Environment["ANTHROPIC_BASE_URL"]   = ollamaBaseUrl.TrimEnd('/');
			psi.Environment["ANTHROPIC_AUTH_TOKEN"] = Constants.Ollama.AuthToken;
			psi.Environment["ANTHROPIC_API_KEY"]    = Constants.Ollama.AuthToken;
		}
		else
		{
			// Anthropic routing: set profile isolation and optional proxy.
			if (!string.IsNullOrEmpty(profileConfigDir))
				psi.Environment["CLAUDE_CONFIG_DIR"] = profileConfigDir;

			var httpsProxy = _appSettings.Settings.HttpsProxy;
			if (!string.IsNullOrWhiteSpace(httpsProxy))
			{
				psi.Environment["HTTPS_PROXY"] = httpsProxy;
				psi.Environment["HTTP_PROXY"]  = httpsProxy;
				psi.Environment["NODE_TLS_REJECT_UNAUTHORIZED"] = "0";
				_log.Information("HTTPS proxy configured: {Proxy}", httpsProxy);
			}
		}

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

			var blockTypeStr = blockType.GetString();
			if (blockTypeStr == "text" && block.TryGetProperty("text", out var text))
			{
				sb.Append(text.GetString());
			}
			else if (blockTypeStr == "tool_use")
			{
				var toolName = block.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null;
				if (toolName == "AskUserQuestion")
				{
					var questionMd = FormatAskUserQuestion(block);
					if (!string.IsNullOrEmpty(questionMd))
					{
						if (sb.Length > 0) sb.AppendLine();
						sb.Append(questionMd);
					}
				}
			}
		}

		return sb.Length > 0 ? sb.ToString().TrimEnd() : null;
	}

	private static string? FormatAskUserQuestion(JsonElement block)
	{
		if (!block.TryGetProperty("input", out var input))
			return null;
		if (!input.TryGetProperty("questions", out var questions) ||
		    questions.ValueKind != JsonValueKind.Array)
			return null;

		var sb = new StringBuilder();
		foreach (var q in questions.EnumerateArray())
		{
			if (!q.TryGetProperty("question", out var questionEl))
				continue;
			var questionText = questionEl.GetString();
			if (string.IsNullOrEmpty(questionText))
				continue;

			if (sb.Length > 0)
				sb.AppendLine();

			sb.AppendLine($"**{questionText}**");

			if (q.TryGetProperty("options", out var options) &&
			    options.ValueKind == JsonValueKind.Array)
			{
				foreach (var opt in options.EnumerateArray())
				{
					var label = opt.TryGetProperty("label", out var lEl) ? lEl.GetString() : null;
					var desc  = opt.TryGetProperty("description", out var dEl) ? dEl.GetString() : null;

					if (string.IsNullOrEmpty(label))
						continue;

					sb.Append($"- **{label}**");
					if (!string.IsNullOrEmpty(desc))
						sb.Append($" — {desc}");
					sb.AppendLine();
				}
			}
		}

		return sb.Length > 0 ? sb.ToString().TrimEnd() : null;
	}
}
