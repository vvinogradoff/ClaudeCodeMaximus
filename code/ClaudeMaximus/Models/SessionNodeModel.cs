namespace ClaudeMaximus.Models;

/// <summary>
/// Terminal tree node representing one Claude Code session.
/// Name is user-assigned and stored in appsettings.json.
/// FileName is the bare file name (e.g. 2026-03-12-1430-xkqbzf.txt) relative to SessionFilesRoot.
/// WorkingDirectory is the filesystem path used when launching the claude process.
/// ClaudeSessionId is captured from the first result event and used for --resume on subsequent launches.
/// ExternalId is the stable daemon-side identifier (UUID from JSONL). Used for all Tessyn daemon references.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class SessionNodeModel
{
	public required string Name { get; set; }
	public required string FileName { get; init; }
	public string WorkingDirectory { get; set; } = string.Empty;
	public string? ClaudeSessionId { get; set; }

	/// <summary>
	/// Stable session identifier for the Tessyn daemon (UUID from JSONL filename or session_id).
	/// Populated from ClaudeSessionId during migration, or from run.system event for new sessions.
	/// Null for sessions not yet mapped to the daemon.
	/// </summary>
	public string? ExternalId { get; set; }

	/// <summary>Persisted vertical scroll offset for the session output area.</summary>
	public double ScrollOffset { get; set; }

	/// <summary>Per-session auto-commit toggle (FR.11.3). Persisted across app restarts.</summary>
	public bool IsAutoCommit { get; set; }

	/// <summary>Per-session auto-document toggle (FR.11.5). Persisted across app restarts.</summary>
	public bool IsAutoDocument { get; set; }

	/// <summary>
	/// Returns the best available session identity key: ExternalId if available, otherwise FileName.
	/// Used for cache keying and session restore during the migration period.
	/// </summary>
	public string SessionKey => ExternalId ?? FileName;
}
