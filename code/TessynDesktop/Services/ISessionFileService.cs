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
	/// <summary>Creates a new session file and returns its bare file name.</summary>
	string CreateSessionFile();

	void AppendMessage(string fileName, string role, string content);
	void AppendCompactionSeparator(string fileName);

	IReadOnlyList<SessionEntryModel> ReadEntries(string fileName);

	bool SessionFileExists(string fileName);

	/// <summary>Deletes the Maximus session .txt file if it exists. Does NOT touch the Claude JSONL.</summary>
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
