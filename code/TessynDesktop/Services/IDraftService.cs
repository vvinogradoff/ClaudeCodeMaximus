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
}
