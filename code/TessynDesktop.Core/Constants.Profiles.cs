namespace TessynDesktop;

/// <remarks>Created by Claude</remarks>
public static partial class Constants
{
	/// <summary>Profile pins, profile switching and the session migration between profile config dirs (ISSUE.013).</summary>
	public static class ProfileSwitch
	{
		/// <summary>The id a session is pinned to when it runs on the Default (system) profile.</summary>
		public const string DefaultProfileId = Remote.DefaultOptionId;

		/// <summary>The profile name shown for the Default profile in listings and messages.</summary>
		public const string DefaultProfileName = "default";

		/// <summary>At most this many absolute paths under the old config dir are reported by one migration.</summary>
		public const int MaxReportedPaths = 50;

		/// <summary>At most this many registered directories are named in the spawn error (R9).</summary>
		public const int MaxListedDirectories = 12;

		public const string UnpinnedMissingProfileLog =
			"Session {NodeId} is pinned to profile name '{ProfileName}', which matches no stored profile; the pin is left as it is";

		public const string PinMigratedLog = "Migrated {Count} session profile pin(s) from display name to ProfileId; {Unmatched} left unmatched";

		public const string SessionMovedLog =
			"Moved Claude session {SessionId} (slug {Slug}) from {Source} to {Target}: transcript={Transcript}, folder files={FolderFiles}, old-config-dir paths in transcript={PathCount}";

		public const string PinnedProfileMissingFormat =
			"Session '{0}' is pinned to profile '{1}', which no longer exists (removed or renamed). It does not fall back to the Default profile.";

		public const string PinnedProfileLoggedOutFormat =
			"Session '{0}' is pinned to profile '{1}', which is logged out. Re-authenticate that profile in Tessyn.";

		public const string MigrateHint =
			" Call send_to_session with migrate=true (allowed only for your own descendants) to move it to your profile, or re-authenticate the profile.";

		public const string NotUnderProjectFormat =
			"'{0}' is not under a project in the Tessyn tree. Registered: {1}. Add it in Tessyn or use parentNodeId.";

		/// <summary>Appended to the profile shown for a session pinned to a profile that no longer exists.</summary>
		public const string MissingProfileSuffix = " (missing)";

		public const string NoRegisteredDirectories = "(none)";
	}
}
