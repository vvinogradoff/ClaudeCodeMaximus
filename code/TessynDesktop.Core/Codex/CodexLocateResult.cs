namespace TessynDesktop.Codex;

/// <summary>
/// The outcome of <see cref="CodexExecutableLocator.Locate"/>. <see cref="ExecutablePath"/> is the native exe to launch (a link already
/// resolved to its target); <see cref="DiscoveredPath"/> is where it was found. <see cref="Problem"/> says why nothing usable was found.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed record CodexLocateResult(string? ExecutablePath, string? DiscoveredPath, CodexExecutableSource Source, string? Problem)
{
	public bool Found => ExecutablePath is not null;

	public static CodexLocateResult NotFound(string problem) => new(null, null, CodexExecutableSource.None, problem);
}
