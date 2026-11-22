using System.Collections.Generic;

namespace TessynDesktop.Codex;

/// <summary>An app-server that exited unexpectedly (FR.33.6): its exit code (null if it could not be read) and the last lines of its stderr.</summary>
/// <remarks>Created by Claude</remarks>
public sealed record CodexHostCrashInfo(string ProfileId, int? ExitCode, IReadOnlyList<string> StderrTail);
