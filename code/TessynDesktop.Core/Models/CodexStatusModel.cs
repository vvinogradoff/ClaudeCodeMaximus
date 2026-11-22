namespace TessynDesktop.Models;

/// <summary>
/// Settings > General: Codex (FR.33.17, FR.33.6): whether Codex was found, the native exe and where it was found, its version, the tested version,
/// the version note (null when it is the tested one) and the status line to show.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed record CodexStatusModel(
	bool Found,
	string? ExecutablePath,
	string? DiscoveredPath,
	string Source,
	string? Version,
	string TestedVersion,
	string? Note,
	string StatusLine);
