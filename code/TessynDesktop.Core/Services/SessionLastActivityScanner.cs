using System;
using System.Collections.Generic;
using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <summary>
/// Reads the last user prompt, last assistant response, prompt profile and draft timestamp for session files.
/// Pure file work with no UI state, so callers run it on a background thread (ISSUE.016).
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class SessionLastActivityScanner : ISessionLastActivityScanner
{
	private readonly ISessionFileService _sessionFiles;
	private readonly IDraftService _drafts;

	public SessionLastActivityScanner(ISessionFileService sessionFiles, IDraftService drafts)
	{
		_sessionFiles = sessionFiles;
		_drafts = drafts;
	}

	public IReadOnlyList<SessionLastActivityModel> Scan(IReadOnlyList<string> sessionFileNames)
	{
		var results = new List<SessionLastActivityModel>(sessionFileNames.Count);
		foreach (var fileName in sessionFileNames)
			results.Add(ScanOne(fileName));
		return results;
	}

	private SessionLastActivityModel ScanOne(string fileName)
	{
		SessionEntryModel? lastUserEntry = null;
		DateTimeOffset? lastAssistant = null;

		try
		{
			var entries = _sessionFiles.ReadEntries(fileName);
			for (var i = entries.Count - 1; i >= 0 && (lastUserEntry == null || lastAssistant == null); i--)
			{
				if (lastUserEntry == null && entries[i].Role == Constants.SessionFile.RoleUser)
					lastUserEntry = entries[i];
				else if (lastAssistant == null && entries[i].Role == Constants.SessionFile.RoleAssistant)
					lastAssistant = entries[i].Timestamp;
			}
		}
		catch
		{
			// Session file may not exist or be corrupted; leave timestamps null.
		}

		DateTimeOffset? draftTimestamp = null;
		try
		{
			// A draft file can persist with whitespace-only content (e.g. all text deleted without
			// clearing the draft); that isn't a real pending draft, so its timestamp must not count.
			var draftText = _drafts.LoadDraft(fileName);
			if (!string.IsNullOrWhiteSpace(draftText))
				draftTimestamp = _drafts.GetDraftTimestamp(fileName);
		}
		catch
		{
			// Unreadable draft: treat as no draft.
		}

		return new SessionLastActivityModel(fileName, lastUserEntry?.Timestamp, lastAssistant, lastUserEntry?.ProfileName, draftTimestamp);
	}
}
