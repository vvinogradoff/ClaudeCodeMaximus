using System;
using System.Collections.Generic;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;

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

	/// <summary>
	/// The reply draft of a Focus item with its question values (FR.23.37), or null if none exists. <see cref="LoadItemDraft"/> returns the note only. The
	/// default is for fakes that keep no question values.
	/// </summary>
	ItemDraftModel? LoadItemDraftFull(string sessionFileName, string itemId)
	{
		var text = LoadItemDraft(sessionFileName, itemId);
		return text == null ? null : new ItemDraftModel(text, null);
	}

	/// <summary>Saves the note and the question values of a Focus item's reply draft. Overwrites any existing one.</summary>
	void SaveItemDraftFull(string sessionFileName, string itemId, string text, IReadOnlyList<AttentionQuestionAnswerParams>? questions) =>
		SaveItemDraft(sessionFileName, itemId, text);

	/// <summary>Compare-and-write like <see cref="TryWriteItemDraft"/>, with the question values; an empty note without question values deletes the draft.</summary>
	bool TryWriteItemDraftFull(string sessionFileName, string itemId, string text, IReadOnlyList<AttentionQuestionAnswerParams>? questions, Func<ItemDraftModel?, bool> isExpected)
	{
		if (!isExpected(LoadItemDraftFull(sessionFileName, itemId)))
			return false;

		if (text.Length == 0 && questions is not { Count: > 0 })
			DeleteItemDraft(sessionFileName, itemId);
		else
			SaveItemDraftFull(sessionFileName, itemId, text, questions);
		return true;
	}

	/// <summary>The ids of the Focus items of a session that have a reply draft.</summary>
	IReadOnlyList<string> ListItemDraftIds(string sessionFileName) => [];

	/// <summary>The Focus reply drafts of a session with the stamp of each file (FR.9.20). Metadata only: never reads a draft's content.</summary>
	IReadOnlyList<ItemDraftStampModel> ListItemDraftStamps(string sessionFileName) => [];

	/// <summary>Returns when a Focus item's reply draft was last written, or null if none exists.</summary>
	DateTimeOffset? GetItemDraftTimestamp(string sessionFileName, string itemId) => null;

	/// <summary>Deletes every Focus reply draft of a session (the session was deleted).</summary>
	void DeleteItemDrafts(string sessionFileName)
	{
	}

	/// <summary>The draft file's current (length, last write time) stamp, or null when there is no draft file. Never reads content.</summary>
	SessionFileStampModel? GetDraftStamp(string sessionFileName);

	/// <summary>Returns when the draft for a session was last written, or null if none exists.</summary>
	DateTimeOffset? GetDraftTimestamp(string sessionFileName);
}
