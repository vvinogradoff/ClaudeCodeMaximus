using System;
using System.Collections.Generic;
using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <summary>The profile, model and effort a session last used, as recorded in its transcript.</summary>
/// <remarks>Created by Claude</remarks>
public sealed record LastUsedSessionSettings(string? ProfileName, string? ModelId, string? Effort);

/// <summary>
/// Finds a session's last-used profile, model and effort without parsing the whole transcript on every session switch.
/// They are recorded on the newest user (profile) and assistant (model, effort) entries, so a tail window almost always
/// holds them; the window is widened (64 KB, 256 KB, 1 MB, then the whole file) only while one is still missing.
/// </summary>
/// <remarks>Created by Claude</remarks>
public static class LastUsedSessionSettingsReader
{
	public static LastUsedSessionSettings Read(ISessionFileService files, string fileName)
	{
		var length = files.GetFileStamp(fileName)?.Length ?? 0;
		if (length == 0)
			return new LastUsedSessionSettings(null, null, null);

		SessionEntryModel? lastUser = null;
		SessionEntryModel? lastAssistant = null;

		for (var window = (long)Constants.LastUsedSettingsTailBytes; ; window *= 4)
		{
			var wholeFile = window >= length || window > Constants.LastUsedSettingsMaxTailBytes;
			var entries = wholeFile ? files.ReadEntries(fileName) : files.ReadTailEntries(fileName, (int)window);

			lastUser = FindLast(entries, Constants.SessionFile.RoleUser, e => e.ProfileName != null);
			lastAssistant = FindLast(entries, Constants.SessionFile.RoleAssistant, e => e.ModelId != null);

			if (wholeFile || (lastUser != null && lastAssistant != null))
				break;
		}

		return new LastUsedSessionSettings(lastUser?.ProfileName, lastAssistant?.ModelId, lastAssistant?.Effort);
	}

	private static SessionEntryModel? FindLast(IReadOnlyList<SessionEntryModel> entries, string role, Func<SessionEntryModel, bool> predicate)
	{
		for (var i = entries.Count - 1; i >= 0; i--)
		{
			if (entries[i].Role == role && predicate(entries[i]))
				return entries[i];
		}

		return null;
	}
}
