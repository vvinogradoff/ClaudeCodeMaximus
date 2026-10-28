using System;
using System.Collections.Generic;
using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <summary>
/// Creates, reads and appends to session text files.
/// Each session is stored as a plain text file in the SessionFilesRoot.
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface ISessionFileService
{
	/// <summary>Raised after a single entry is appended (FR.21.9) — see <see cref="SessionEntryAppendedEventArgs"/>.</summary>
	event EventHandler<SessionEntryAppendedEventArgs>? EntryAppended;

	/// <summary>Raised once per turn after its last entry is appended (FR.21.22) — see <see cref="SessionTurnCompletedEventArgs"/>.</summary>
	event EventHandler<SessionTurnCompletedEventArgs>? TurnCompleted;

	/// <summary>
	/// Reports that a turn on <paramref name="fileName"/> has ended and all of its entries have been
	/// appended (FR.21.22). Raises <see cref="TurnCompleted"/>; writes nothing to the file.
	/// </summary>
	void NotifyTurnCompleted(string fileName, bool cancelled, string? errorText);

	/// <summary>Creates a new session file and returns its bare file name.</summary>
	string CreateSessionFile();

	/// <summary>
	/// Appends a message entry to the session file. Optional metadata is embedded in the header line:
	/// <paramref name="profileName"/> for USER entries, <paramref name="modelId"/> and <paramref name="effort"/> for ASSISTANT entries.
	/// </summary>
	void AppendMessage(string fileName, string role, string content, string? profileName = null, string? modelId = null, string? effort = null);
	void AppendCompactionSeparator(string fileName);

	IReadOnlyList<SessionEntryModel> ReadEntries(string fileName);

	bool SessionFileExists(string fileName);

	/// <summary>Deletes the Tessyn session .txt file if it exists. Does NOT touch the Claude JSONL.</summary>
	void DeleteSessionFile(string fileName);

	/// <summary>Returns the full filesystem path for a session file name.</summary>
	string GetFullPath(string fileName);

	/// <summary>Atomically rewrites the session file with new content (write .tmp then rename).</summary>
	void RewriteSessionFile(string fileName, string content);

	/// <summary>
	/// Writes a complete session file from a list of parsed session entries.
	/// Used for session import (FR.13.11). The file is created and written atomically.
	/// </summary>
	void WriteSessionFile(string fileName, IReadOnlyList<SessionEntryModel> entries);

	/// <summary>
	/// Scans all session files and repairs any that were corrupted by the auto-compaction bug
	/// (raw text without [timestamp] ROLE headers). Returns the count of repaired files.
	/// </summary>
	int RepairCorruptedCompactions();
}
