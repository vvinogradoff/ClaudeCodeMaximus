using System;
using System.Collections.Generic;
using Serilog;
using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <summary>
/// ISSUE.013: converts the display-name pins of existing sessions to <see cref="SessionNodeModel.ProfileId"/> pins when the settings
/// load (match by name). A pin that matches no stored profile is left as it is and logged; <c>[Sys]</c> labels (the Default profile) stay unpinned by id.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class ProfilePinMigration
{
	private static readonly ILogger _log = Log.ForContext<ProfilePinMigration>();

	/// <summary>Migrates every session of <paramref name="settings"/>. Returns the number of pins converted.</summary>
	public int Migrate(AppSettingsModel settings)
	{
		var migrated  = 0;
		var unmatched = 0;

		foreach (var directory in settings.Tree)
			Visit(directory.Sessions, directory.Groups, settings.Profiles, ref migrated, ref unmatched);

		if (migrated > 0 || unmatched > 0)
			_log.Information(Constants.ProfileSwitch.PinMigratedLog, migrated, unmatched);
		return migrated;
	}

	private static void Visit(
		List<SessionNodeModel> sessions, List<GroupNodeModel> groups, List<ClaudeProfileModel> profiles,
		ref int migrated, ref int unmatched)
	{
		foreach (var session in sessions)
		{
			if (!string.IsNullOrEmpty(session.ProfileId) || string.IsNullOrEmpty(session.ProfileName))
				continue;
			if (session.ProfileName.StartsWith(Constants.Remote.Profiles.SystemPrefix, StringComparison.Ordinal))
				continue; // the Default profile's label

			var match = profiles.Find(p => string.Equals(p.DisplayName, session.ProfileName, StringComparison.OrdinalIgnoreCase));
			if (match == null)
			{
				unmatched++;
				_log.Warning(Constants.ProfileSwitch.UnpinnedMissingProfileLog, session.NodeId, session.ProfileName);
				continue;
			}

			session.ProfileId = match.ProfileId;
			migrated++;
		}

		foreach (var group in groups)
			Visit(group.Sessions, group.Groups, profiles, ref migrated, ref unmatched);
	}
}
