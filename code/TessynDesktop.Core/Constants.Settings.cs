namespace TessynDesktop;

/// <remarks>Created by Claude</remarks>
public static partial class Constants
{
	/// <summary>Limits and messages of the settings sections and profile administration (package J3).</summary>
	public static class SettingsApi
	{
		/// <summary>The first version of a section; a counter that restarts with the daemon.</summary>
		public const long InitialVersion = 1;

		/// <summary>The message of a <c>settings.update</c> whose <c>baseVersion</c> is not the current version.</summary>
		public const string StaleVersionMessage = "Settings changed";

		/// <summary>Most entries a list setting (media preview hosts, Telegram user ids) may hold.</summary>
		public const int MaxListEntries = 200;

		/// <summary>Longest host pattern of the media preview list (a DNS name is at most 253 characters).</summary>
		public const int MaxHostLength = 253;

		/// <summary>Longest path, URL or command a text setting may hold.</summary>
		public const int MaxTextLength = 1024;

		/// <summary>The most seconds of the completion-sound throttle (one day).</summary>
		public const int MaxSoundThrottleSeconds = 24 * 60 * 60;

		/// <summary>Largest byte limit a file-access cap may be raised to (1 TiB).</summary>
		public const long MaxFileCapBytes = 1L << 40;

		/// <summary>Largest chunk one relay <c>file.read</c> may return (16 MiB).</summary>
		public const int MaxRelayChunkBytes = 16 * 1024 * 1024;

		public const string WavExtension = ".wav";
		public const string MentionHandlePrefix = "@";
		public const int MaxMentionHandleLength = 65;

		/// <summary>FR.33.17: the Codex path must name a native executable (never the <c>codex.cmd</c> or Node shim).</summary>
		public const string ExeExtension = ".exe";

		/// <summary>FR.33.14: the longest match text of a GPT tier row, and the tier range (0 local, 1 Haiku, 2 Sonnet, 3 Opus, 4 Fable).</summary>
		public const int MaxTierMatchLength = 64;
		public const int MinModelTier = 0;
		public const int MaxModelTier = 4;

		/// <summary>How long <c>settings.get</c> waits for <c>codex --version</c> before it answers the status without a version.</summary>
		public static readonly TimeSpan CodexStatusTimeout = TimeSpan.FromSeconds(10);

	}

	/// <summary>Profile administration (package J3).</summary>
	public static class ProfileAdmin
	{
		/// <summary>Prefix of a generated profile id (<c>profile_1</c>, <c>profile_2</c>...), also the config subdirectory name.</summary>
		public const string IdPrefix = "profile_";

		/// <summary>The largest profile display name.</summary>
		public const int MaxDisplayNameLength = 200;
	}
}
