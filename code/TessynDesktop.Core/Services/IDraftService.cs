using System;
using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <summary>
/// Persists per-session input draft text across application restarts.
/// Drafts are stored in the application data folder as small text files.
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface IDraftService
{
	/// <summary>Loads the saved draft for a session, or null if none exists.</summary>
	string? LoadDraft(string sessionFileName);

	/// <summary>Saves the draft for a session. Overwrites any existing draft.</summary>
	void SaveDraft(string sessionFileName, string text);

	/// <summary>Deletes the draft for a session (e.g. after the message is sent).</summary>
	void DeleteDraft(string sessionFileName);

	/// <summary>The draft file's current (length, last write time) stamp, or null when there is no draft file. Never reads content.</summary>
	SessionFileStampModel? GetDraftStamp(string sessionFileName);

	/// <summary>Returns when the draft for a session was last written, or null if none exists.</summary>
	DateTimeOffset? GetDraftTimestamp(string sessionFileName);
}
