using System.Collections.Generic;
using ClaudeMaximus.Models;

namespace ClaudeMaximus.Services;

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
}
