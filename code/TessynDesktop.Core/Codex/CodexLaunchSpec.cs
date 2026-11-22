using System.Collections.Generic;

namespace TessynDesktop.Codex;

/// <summary>What <see cref="ICodexProcessLauncher"/> starts: <c>&lt;exe&gt; app-server [-c key=value]…</c>, hidden, with <c>CODEX_HOME</c> when set.</summary>
/// <remarks>Created by Claude</remarks>
public sealed record CodexLaunchSpec(string ExecutablePath, IReadOnlyList<string> Arguments, string? CodexHome, string WorkingDirectory);
