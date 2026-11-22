using System;
using TessynDesktop.Services;

namespace TessynDesktop.Tests.Services;

/// <summary>
/// Builds the session-engine router (FR.33.1) over the Claude fakes a test already has, so the code under test reaches the fakes through the
/// seam exactly as it reached them directly before.
/// </summary>
/// <remarks>Created by Claude</remarks>
internal static class TestEngines
{
	/// <summary>A router with the Claude engine over the given fakes.</summary>
	public static SessionEngineRegistry Claude(
		IClaudeStreamingSessionManager streaming,
		IClaudeProcessManager process,
		ISessionFileService files,
		IAppSettingsService settings,
		IClaudeSessionStatusService status,
		Lazy<IAgentMcpServer> mcp) =>
		new([new ClaudeSessionEngine(process, streaming, files, settings, status, mcp)]);

	/// <summary>
	/// A router for code that only asks whether a session's persistent process is alive and stops it (state builders, RPC modules, the profile
	/// switch). The Claude engine gets no turn dependencies: running a turn or "Terminate all" through this router is not supported.
	/// </summary>
	public static SessionEngineRegistry Claude(IClaudeStreamingSessionManager streaming) =>
		new([new ClaudeSessionEngine(null!, streaming, null!, null!, null!, new Lazy<IAgentMcpServer>(() => null!))]);
}
