using System;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <summary>
/// Launches claude CLI processes and streams their output as parsed events.
/// Uses per-message process spawning with --resume for session continuity.
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface IClaudeProcessManager
{
	/// <summary>Number of claude processes currently running.</summary>
	int ActiveProcessCount { get; }

	/// <summary>
	/// Spawns a claude process for one user turn. Writes userMessage to stdin, reads
	/// stdout as stream-json events, calls onEvent for each parsed event, then exits.
	/// </summary>
	Task SendMessageAsync(
		string workingDirectory,
		string claudePath,
		string? sessionId,
		string userMessage,
		Action<ClaudeStreamEvent> onEvent,
		string? model = null,
		string? profileConfigDir = null,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Runs claude in print mode for non-interactive utility calls (title generation, search).
	/// Returns the raw stdout text. Uses --tools "" --no-session-persistence --output-format json.
	/// Returns null if the process fails to start or exits with an error.
	/// </summary>
	Task<string?> RunPrintModeAsync(
		string claudePath,
		string prompt,
		string? model = null,
		int timeoutMs = 60000,
		CancellationToken cancellationToken = default);

	/// <summary>Kills all active claude processes immediately.</summary>
	void TerminateAll();
}
