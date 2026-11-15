namespace TessynDesktop.WPF;

/// <summary>Limits and texts of the composer completion (W1b): drafts, autocomplete, the completion sound and the auth-error notice.</summary>
/// <remarks>Created by Claude</remarks>
public static partial class Constants
{
	/// <summary>Timing of the draft sync and the autocomplete popup.</summary>
	public static class ComposerLimits
	{
		/// <summary>The input must stay unchanged this long before its text is written as the session's draft.</summary>
		public static readonly TimeSpan DraftSaveDelay = TimeSpan.FromMilliseconds(800);

		/// <summary>First pause before a draft write that failed is tried again; it doubles per failure.</summary>
		public static readonly TimeSpan DraftRetryBackoff = TimeSpan.FromSeconds(2);

		/// <summary>Longest pause between two tries of a failing draft write.</summary>
		public static readonly TimeSpan DraftMaxRetryBackoff = TimeSpan.FromSeconds(30);

		/// <summary>The input must stay unchanged this long before the daemon is asked for suggestions.</summary>
		public static readonly TimeSpan AutocompleteDebounce = TimeSpan.FromMilliseconds(150);

		/// <summary>Pause before asking again while the daemon builds the code index of the session's directory (<c>indexReady: false</c>).</summary>
		public static readonly TimeSpan AutocompleteIndexRetry = TimeSpan.FromMilliseconds(400);

		/// <summary>Asks again at most this many times for one input state (about 30 s at the retry pause).</summary>
		public const int AutocompleteIndexRetries = 75;

		/// <summary>Suggestions shown at most (the daemon caps its own answer too).</summary>
		public const int MaxSuggestionsShown = 50;
	}

	/// <summary>Texts of the autocomplete popup, the draft sync and the auth-error notice.</summary>
	public static class ComposerTexts
	{
		public const string IndexBuilding = "Building the code index for suggestions...";
		public const string AuthErrorHeading = "Sign-in needed";
		public const string AuthErrorMessage = "This session's profile is not signed in. Sign in again, then send your message.";
		public const string Reauthenticate = "Re-authenticate";
		public const string ReauthenticateStarted = "A sign-in window was opened. Finish the login there.";
		public const string ReauthenticateRunning = "A sign-in for this profile is already running.";
		public const string ReauthenticateFailed = "Could not start the sign-in: ";
	}

	/// <summary>The completion sound (FR.22): what the client reads from the daemon and when it stays silent.</summary>
	public static class SoundRules
	{
		/// <summary>Channel this client registers for: the host stays silent for it while this client is connected.</summary>
		public const string Channel = TessynDesktop.Models.Remote.RemoteProtocol.Notifications.SoundChannel;

		/// <summary>The daemon's notification settings are read again after this long (a change in Settings applies without a restart).</summary>
		public static readonly TimeSpan SettingsCacheAge = TimeSpan.FromSeconds(30);
	}
}
