using System;
using System.Collections.Generic;
using System.Linq;
using Serilog;
using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <summary>
/// ISSUE.013: converts the display-name pins of existing sessions to <see cref="SessionNodeModel.ProfileId"/> pins when the settings
/// load (match by name; with duplicate display names the first profile wins and the duplicate is logged). A name that matches no
/// stored profile is left as it is (it runs on Default with a warning, see <see cref="IProfileResolver"/>); <c>[Sys]</c> labels stay unpinned by id.
/// One summary line is logged. Never throws: a failure is logged and the settings load goes on.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class ProfilePinMigration
{
	private static readonly ILogger _log = Log.ForContext<ProfilePinMigration>();

	/// <summary>Migrates every session of <paramref name="settings"/> and logs one summary line. Returns the summary.</summary>
	public ProfilePinMigrationSummary Migrate(AppSettingsModel settings)
	{
		var counts = new Counts();
		try
		{
			var profiles = settings.Profiles ?? [];
			var duplicates = profiles.GroupBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
			foreach (var name in duplicates)
				_log.Warning("Profile display name '{Name}' is used by more than one stored profile; pins by that name go to the first", name);

			foreach (var directory in settings.Tree ?? [])
				Visit(directory.Sessions, directory.Groups, profiles, counts);
		}
		catch (Exception ex)
		{
			_log.Warning(ex, "Migrating the session profile pins failed; the settings load goes on");
		}

		_log.Information(Constants.ProfileSwitch.PinMigratedLog, counts.Migrated, counts.Unmatched, counts.SystemLabels);
		return new ProfilePinMigrationSummary(counts.Migrated, counts.Unmatched, counts.SystemLabels);
	}

	private sealed class Counts
	{
		public int Migrated;
		public int Unmatched;
		public int SystemLabels;
	}

	private static void Visit(List<SessionNodeModel>? sessions, List<GroupNodeModel>? groups, List<ClaudeProfileModel> profiles, Counts counts)
	{
		foreach (var session in sessions ?? [])
		{
			if (!string.IsNullOrEmpty(session.ProfileId) || string.IsNullOrEmpty(session.ProfileName))
				continue;
			if (session.ProfileName.StartsWith(Constants.Remote.Profiles.SystemPrefix, StringComparison.Ordinal))
			{
				counts.SystemLabels++; // the Default profile's label
				continue;
			}

			var match = profiles.Find(p => string.Equals(p.DisplayName, session.ProfileName, StringComparison.OrdinalIgnoreCase));
			if (match == null)
			{
				counts.Unmatched++;
				continue;
			}

			session.ProfileId = match.ProfileId;
			counts.Migrated++;
		}

		foreach (var group in groups ?? [])
			Visit(group.Sessions, group.Groups, profiles, counts);
	}
}
