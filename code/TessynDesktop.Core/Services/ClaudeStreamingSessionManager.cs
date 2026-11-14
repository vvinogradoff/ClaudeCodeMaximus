using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Models;
using Serilog;

namespace TessynDesktop.Services;

/// <remarks>Created by Claude</remarks>
public sealed class ClaudeStreamingSessionManager : IClaudeStreamingSessionManager
{
	private static readonly ILogger _log = Log.ForContext<ClaudeStreamingSessionManager>();

	private sealed class Handle
	{
		public required Process Process;
		public required Action<ClaudeStreamEvent> OnEvent;
		public readonly object Lock = new();
		public TaskCompletionSource<bool>? PendingTurn;
		/// <summary>Additive, per-turn observer — see <see cref="IClaudeStreamingSessionManager.SendTurnAsync"/>.</summary>
		public Action<ClaudeStreamEvent>? TurnObserver;
	}

	private readonly ClaudeProcessLauncher _launcher;
	private readonly ConcurrentDictionary<string, Handle> _handles = new();

	public event Action<string>? ProcessExited;

	public ClaudeStreamingSessionManager(ClaudeProcessLauncher launcher)
	{
		_launcher = launcher;
	}

	public bool EnsureStarted(string nodeId, StreamingSpawnParams spawnParams, Action<ClaudeStreamEvent> onEvent)
	{
		if (_handles.TryGetValue(nodeId, out var existing))
		{
			existing.OnEvent = onEvent;
			return false;
		}

		var args = BuildStreamingArguments(spawnParams);
		_log.Debug("Spawning streaming claude process for node {NodeId}. Args={Args}", nodeId, args);

		var process = _launcher.Start(spawnParams.ClaudePath, args, spawnParams.WorkingDirectory,
			spawnParams.ProfileConfigDir, spawnParams.OllamaBaseUrl);
		if (process == null)
		{
			_log.Error("Failed to start streaming claude process for node {NodeId}. Path={ClaudePath}",
				nodeId, spawnParams.ClaudePath);
			onEvent(new ClaudeStreamEvent
			{
				Type    = "system",
				Subtype = "error",
				IsError = true,
				Content = $"Could not launch claude at '{spawnParams.ClaudePath}' in Streaming Session Mode.",
			});
			throw new InvalidOperationException("Failed to start streaming claude process");
		}

		var handle = new Handle { Process = process, OnEvent = onEvent };
		_handles[nodeId] = handle;

		_ = ReadLoopAsync(nodeId, handle);
		_ = DrainStderrAsync(nodeId, handle);

		return true;
	}

	public async Task SendTurnAsync(string nodeId, string userMessage, CancellationToken cancellationToken, Action<ClaudeStreamEvent>? onTurnEvent = null)
	{
		if (!_handles.TryGetValue(nodeId, out var handle))
			throw new InvalidOperationException($"No streaming process is alive for node {nodeId}");

		var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		lock (handle.Lock) { handle.PendingTurn = tcs; handle.TurnObserver = onTurnEvent; }

		var line = BuildStreamInputLine(userMessage);
		_log.Debug("Writing streaming turn to node {NodeId} stdin ({Length} chars)", nodeId, userMessage.Length);
		await handle.Process.StandardInput.WriteLineAsync(line);
		await handle.Process.StandardInput.FlushAsync(cancellationToken);

		using var reg = cancellationToken.Register(() => tcs.TrySetCanceled());
		await tcs.Task;
	}

	public bool IsAlive(string nodeId)
	{
		if (!_handles.TryGetValue(nodeId, out var handle))
			return false;
		try { return !handle.Process.HasExited; }
		catch { return false; }
	}

	public void Stop(string nodeId)
	{
		if (!_handles.TryGetValue(nodeId, out var handle))
			return;

		_log.Information("Stopping streaming claude process for node {NodeId}", nodeId);
		try { handle.Process.Kill(entireProcessTree: true); }
		catch (Exception ex) { _log.Warning(ex, "Failed to kill streaming process for node {NodeId}", nodeId); }
		// Removal from _handles, failing any pending turn, and raising ProcessExited all happen in
		// ReadLoopAsync's finally block once the kill above causes stdout to close — single source
		// of truth for "this process has actually exited," whether self-exited or killed here.
	}

	public void TerminateAll()
	{
		foreach (var nodeId in _handles.Keys)
			Stop(nodeId);
	}

	private async Task ReadLoopAsync(string nodeId, Handle handle)
	{
		try
		{
			string? line;
			while ((line = await handle.Process.StandardOutput.ReadLineAsync()) != null)
			{
				if (string.IsNullOrWhiteSpace(line))
					continue;

				_log.Debug("streaming stdout [{NodeId}]: {Line}", nodeId, line);

				foreach (var evt in ClaudeStreamEventParser.ParseEvents(line))
				{
					handle.OnEvent(evt);

					Action<ClaudeStreamEvent>? observer;
					lock (handle.Lock) observer = handle.TurnObserver;
					observer?.Invoke(evt);

					if (evt.Type == "result")
					{
						TaskCompletionSource<bool>? pending;
						lock (handle.Lock) { pending = handle.PendingTurn; handle.PendingTurn = null; handle.TurnObserver = null; }
						pending?.TrySetResult(true);
					}
				}
			}
		}
		catch (Exception ex)
		{
			_log.Warning(ex, "Streaming read loop ended for node {NodeId}", nodeId);
		}
		finally
		{
			_handles.TryRemove(nodeId, out _);

			TaskCompletionSource<bool>? pending;
			lock (handle.Lock) pending = handle.PendingTurn;
			pending?.TrySetException(new InvalidOperationException("Streaming process exited before this turn completed"));

			try { if (!handle.Process.HasExited) handle.Process.Kill(entireProcessTree: true); }
			catch { /* already gone */ }

			ProcessExited?.Invoke(nodeId);
		}
	}

	private async Task DrainStderrAsync(string nodeId, Handle handle)
	{
		try
		{
			string? line;
			while ((line = await handle.Process.StandardError.ReadLineAsync()) != null)
			{
				if (!string.IsNullOrWhiteSpace(line))
					_log.Warning("streaming stderr [{NodeId}]: {Line}", nodeId, line.Trim());
			}
		}
		catch
		{
			// Process gone — ReadLoopAsync's finally already handles cleanup/notification.
		}
	}

	private static string BuildStreamInputLine(string text)
	{
		var payload = new
		{
			type = "user",
			message = new
			{
				role = "user",
				content = new object[] { new { type = "text", text } },
			},
		};
		return JsonSerializer.Serialize(payload);
	}

	private static string BuildStreamingArguments(StreamingSpawnParams p)
	{
		// --input-format stream-json keeps stdin open for multiple turns (FR.19.2) — verified
		// against the installed CLI (Stage 1 spike) to not exit after emitting a result event,
		// unlike the default text input format used by One-shot Mode.
		var args = "--input-format stream-json --output-format stream-json --verbose --dangerously-skip-permissions -p";
		if (!string.IsNullOrEmpty(p.SessionId))
			args += $" --resume {p.SessionId}";
		if (!string.IsNullOrEmpty(p.Model))
			args += $" --model {p.Model}";
		if (!string.IsNullOrEmpty(p.Effort))
			args += $" --effort {p.Effort}";
		if (!string.IsNullOrEmpty(p.McpConfigPath))
			args += $" --mcp-config \"{p.McpConfigPath}\"";
		if (p.StrictMcpConfig)
			args += " --strict-mcp-config";
		if (p.DisableTools)
			args += " --tools \"\"";
		else if (p.LocalToolSpec != null)
			args += $" --tools \"{string.Join(' ', p.LocalToolSpec.NativeToolNames)}\"";
		if (p.Chrome)
			args += " --chrome";
		// No --disallowed-tools here — Streaming Session Mode omits the native-scheduling hard
		// block (FR.19.10); the redirect itself is also omitted from the instruction block (FR.19.9).
		return args;
	}
}
