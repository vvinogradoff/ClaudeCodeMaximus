namespace TessynDesktop;

public static partial class Constants
{
	/// <summary>The <c>create_session</c> tool (FR.15.15): a session creates a top-level session for the user. Limits are in <see cref="Agent"/>.</summary>
	public static class CreateSession
	{
		public const string ToolName = "create_session";

		public static readonly string ToolDescription =
			"Create a new top-level session for the user - NOT a worker of yours: it has no supervisor, it is listed in the tree, Recent and Telegram like a session the user created, and you get no result from it (use spawn_session for workers). " +
			"Use it only when the user asks for a new session. Place it with after=<session id> (right after that session; find it with list_sessions) or with workingDir and/or group (last there); with neither it goes right after you. " +
			"Settings: model (tier-checked), effort, profile (default: yours), streaming, sound (default on). An optional prompt starts its first turn in the background. Only top-level sessions may call it; at most " + Agent.MaxCreatedSessionsPerHour + " sessions per hour.";

		public const string WorkerRefusalText = "create_session is for top-level sessions: a worker reports to its supervisor instead.";
		public const string NameRequiredText = "'name' is required";
		public const string BlankNameText = "'name' must not be blank.";
		public const string PlacementConflictText = "Use either 'after' or 'workingDir'/'group', not both.";
		public const string AnchorNotFoundText = "Session for 'after' not found";
		public const string NoProfilesText = "Profiles are not available in this host.";

		/// <summary>Arg 0 = the limit per hour.</summary>
		public const string RateCapFormat = "Session creation limit reached: at most {0} sessions per hour. Try again later.";

		/// <summary>Arg 0 = the given model, arg 1 = the catalog's ids.</summary>
		public const string UnknownModelFormat = "Unknown model '{0}'. Models: {1}.";

		/// <summary>Arg 0 = the given effort, arg 1 = the efforts.</summary>
		public const string UnknownEffortFormat = "Unknown effort '{0}'. Efforts: {1}.";

		/// <summary>Arg 0 = the given profile, arg 1 = the profiles.</summary>
		public const string UnknownProfileFormat = "Unknown profile '{0}'. Profiles: {1}.";

		/// <summary>Arg 0 = the profile's display name.</summary>
		public const string LoggedOutProfileFormat = "Profile '{0}' is signed out (no credentials): sign it in again in Tessyn, or pick another profile.";

		/// <summary>Arg 0 = the caller's tier name, arg 1 = the model's tier name, arg 2 = the caller's model, arg 3 = the requested model (FR.16.6).</summary>
		public const string TierViolationFormat = "Tier violation: a {0} session cannot create a {1} session. Caller model: '{2}', requested: '{3}'.";

		/// <summary>The models listed in an unknown-model error; the rest are counted.</summary>
		public const int MaxListedModels = 12;

		/// <summary>Arg 0 = the count of models not listed.</summary>
		public const string MoreModelsFormat = " (+{0} more)";

		/// <summary>Arg 0 = name, arg 1 = NodeId, arg 2 = the place.</summary>
		public const string ResultFormat = "Session '{0}' created (nodeId={1}) in {2}.";

		/// <summary>Arg 0 = the container, arg 1 = the anchor's name.</summary>
		public const string PlaceAfterFormat = "{0}, right after '{1}'";

		/// <summary>Arg 0 = the container.</summary>
		public const string PlaceLastFormat = "{0}, last";

		public const string PathSeparator = " / ";

		/// <summary>Arg 0 = model, arg 1 = effort, arg 2 = profile, arg 3 = streaming, arg 4 = sound.</summary>
		public const string SettingsFormat = "model={0}, effort={1}, profile={2}, streaming={3}, sound={4}.";

		public const string On = "on";
		public const string Off = "off";
		public const string FirstTurnStarted = "First turn started.";
		public const string NoPromptRun = "No prompt run.";
	}
}
