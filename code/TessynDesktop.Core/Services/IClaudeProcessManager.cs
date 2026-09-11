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
	/// Spawns a claude process for one user turn (One-shot Mode, FR.19). Writes userMessage to
	/// stdin, reads stdout as stream-json events, calls onEvent for each parsed event, then exits.
	/// When ollamaBaseUrl is set the process is routed to local Ollama (no profile/proxy).
	/// <paramref name="disallowNativeScheduling"/> passes <c>--disallowed-tools</c> for the native
	/// scheduling tools (FR.19.10), hard-blocking what the instruction-block redirect (FR.14.11)
	/// only advises against. <paramref name="localToolSpec"/> and <paramref name="strictMcpConfig"/>
	/// implement the Local Tool Profile (FR.12.21): when set, <c>--tools</c> is restricted to the
	/// given native tool names (independent of MCP tools — see <c>LocalToolSpec</c>) and
	/// <c>--strict-mcp-config</c> is passed so only the filtered <paramref name="mcpConfigPath"/>
	/// contents are visible, not any ambient project/user MCP config.
	/// </summary>
	Task SendMessageAsync(
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
		string? profileConfigDir = null,
		int timeoutMs = 60000,
		CancellationToken cancellationToken = default);

	/// <summary>Kills all active claude processes immediately.</summary>
	void TerminateAll();
}
