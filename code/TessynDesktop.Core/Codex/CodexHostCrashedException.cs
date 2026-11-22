using System;

namespace TessynDesktop.Codex;

/// <summary>A call failed because its app-server exited unexpectedly (FR.33.6). The next call starts a new process.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class CodexHostCrashedException : Exception
{
	public CodexHostCrashInfo Crash { get; }

	public CodexHostCrashedException(CodexHostCrashInfo crash, Exception? inner = null)
		: base($"Codex app-server for profile {crash.ProfileId} exited unexpectedly (exit {crash.ExitCode?.ToString() ?? "unknown"}).", inner)
	{
		Crash = crash;
	}
}
