namespace TessynDesktop.Codex;

/// <summary>Starts app-server processes. The real one is <see cref="CodexProcessLauncher"/>; tests use an in-memory fake (FR.33.19).</summary>
/// <remarks>Created by Claude</remarks>
public interface ICodexProcessLauncher
{
	ICodexProcess Launch(CodexLaunchSpec spec);
}
