using System;
using System.Collections.Generic;
using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <summary>
/// Persists per-session input draft text across application restarts.
/// Drafts are stored in the application data folder as small text files.
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface IDraftService
{
	/// <summary>Raised after a draft was written or deleted (by any writer), with the session file's name. May be raised on any thread; never raised for a delete of a missing draft.</summary>
	event EventHandler<DraftChangedEventArgs>? DraftChanged
	{
		add { }
		remove { }
	}

	/// <summary>Loads the saved draft for a session, or null if none exists.</summary>
	string? LoadDraft(string sessionFileName);

	/// <summary>Saves the draft for a session. Overwrites any existing draft.</summary>
	void SaveDraft(string sessionFileName, string text);

	/// <summary>
	/// Compare-and-write in one step (V6): writes <paramref name="text"/> (an empty text deletes the draft) only when <paramref name="isExpected"/> accepts the
	/// stored text (null when there is none); false when it does not, and nothing is written. Writers that go through <see cref="SaveDraft"/> and
	/// <see cref="DeleteDraft"/> are serialised with it. The default implementation is not atomic (for fakes).
	/// </summary>
	bool TryWriteDraft(string sessionFileName, string text, Func<string?, bool> isExpected)
	{
		if (!isExpected(LoadDraft(sessionFileName)))
			return false;

		if (text.Length == 0)
			DeleteDraft(sessionFileName);
		else
			SaveDraft(sessionFileName, text);
		return true;
	}

	/// <summary>Deletes the draft for a session (e.g. after the message is sent).</summary>
	void DeleteDraft(string sessionFileName);

	// -- Focus reply drafts (FR.23.19): the unsent reply text of one Focus item, kept beside the session's composer draft. The defaults are for fakes
	// that do not store them.

	/// <summary>Loads the reply draft of a Focus item, or null if none exists. <paramref name="itemId"/> may contain letters, digits, '_' and '-' only.</summary>
	string? LoadItemDraft(string sessionFileName, string itemId) => null;

	/// <summary>Saves the reply draft of a Focus item. Overwrites any existing one.</summary>
	void SaveItemDraft(string sessionFileName, string itemId, string text)
	{
	}

	/// <summary>Deletes the reply draft of a Focus item (sent, or the item closed).</summary>
	void DeleteItemDraft(string sessionFileName, string itemId)
	{
	}

	/// <summary>Compare-and-write of a Focus item's reply draft: like <see cref="TryWriteDraft"/>.</summary>
	bool TryWriteItemDraft(string sessionFileName, string itemId, string text, Func<string?, bool> isExpected)
	{
		if (!isExpected(LoadItemDraft(sessionFileName, itemId)))
			return false;

		if (text.Length == 0)
			DeleteItemDraft(sessionFileName, itemId);
		else
			SaveItemDraft(sessionFileName, itemId, text);
		return true;
	}

	/// <summary>The ids of the Focus items of a session that have a reply draft.</summary>
	IReadOnlyList<string> ListItemDraftIds(string sessionFileName) => [];

	/// <summary>Deletes every Focus reply draft of a session (the session was deleted).</summary>
	void DeleteItemDrafts(string sessionFileName)
	{
	}

	/// <summary>The draft file's current (length, last write time) stamp, or null when there is no draft file. Never reads content.</summary>
	SessionFileStampModel? GetDraftStamp(string sessionFileName);

	/// <summary>Returns when the draft for a session was last written, or null if none exists.</summary>
	DateTimeOffset? GetDraftTimestamp(string sessionFileName);
}
