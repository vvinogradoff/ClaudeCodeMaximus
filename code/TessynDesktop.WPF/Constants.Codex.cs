namespace TessynDesktop.WPF;

/// <summary>Texts and names of the GPT sessions in the WPF client (FR.33): the picker groups, the status bar, Settings > General (Codex) and the ChatGPT profiles.</summary>
/// <remarks>Created by Claude</remarks>
public static partial class Constants
{
	public static class Codex
	{
		/// <summary>The model picker's group headers (FR.33.2), in picker order.</summary>
		public const string ClaudeGroup = "Claude";
		public const string GptGroup = "GPT (ChatGPT plan)";
		public const string LocalGroup = "Local";

		/// <summary>Ids of the picker's non-selectable lines (never sent: they are not options).</summary>
		public const string HeaderIdPrefix = "header:";
		public const string GptHintId = "hint:gpt";

		/// <summary>The line the picker shows when the daemon sends no hint text of its own.</summary>
		public const string GptHintFallback = "GPT models: sign in to ChatGPT in Settings > Profiles";

		/// <summary>FR.33.2: the upgrade hint in a GPT model's tooltip.</summary>
		public const string UpgradeFormat = "Codex suggests {0}";

		/// <summary>FR.33.10: the status bar's left section for a GPT model.</summary>
		public const string ChatGptPlanSuffix = "ChatGPT plan";

		/// <summary>FR.33.10: the tooltip of a window that ran out of credits; {0} is Codex's reason, {1} the reset time.</summary>
		public const string ExhaustedToolTipFormat = "ChatGPT plan limit reached ({0}). This window resets {1:ddd HH:mm}.";
		public const string ExhaustedReasonUnknown = "out of credits";

		/// <summary>A window no longer than this many minutes shows its reset as a time of day, a longer one as a weekday (FR.18.3 format).</summary>
		public const int ShortWindowMaxMinutes = 24 * 60;
		public const int FiveHourMinutes = 300;
		public const int SevenDayMinutes = 10080;
		public const string WindowFormat = "{0}: {1:0}%";
		public const string WindowShortResetFormat = "{0}: {1:0}%  resets {2:HH:mm}";
		public const string WindowLongResetFormat = "{0}: {1:0}%  resets {2:ddd}";

		// Settings > General: Codex (FR.33.17).
		public const string NoCodexEngine = "This daemon has no Codex engine.";
		public const string TestedVersionFormat = "Tested with Codex {0}.";
		public const string AutomaticCatalogProfile = "Automatic (Codex Default, else the first signed-in profile)";
		public const string BrowseCodexTitle = "Choose codex.exe";
		public const string BrowseCodexFilter = "Codex (codex.exe)|codex.exe|Programs (*.exe)|*.exe";
		public const string TiersResetPending = "The defaults are restored when you save.";
		public const string TiersAreDefaults = "This is the built-in table.";
		public const string NewTierMatch = "gpt";
		public const int NewTierLevel = 2;
		public const int MaxTierRows = 200;
		public const int MaxTierMatchChars = 64;
		public const int MinTier = 0;
		public const int MaxTier = 4;
		public const string TierMatchRequired = "Every GPT tier row needs a text to match.";
		public const string TierMatchTooLongFormat = "A GPT tier match may have at most {0} characters.";
		public const string TierMatchDuplicateFormat = "The GPT tier match '{0}' is listed twice.";
		public const string TooManyTierRowsFormat = "At most {0} GPT tier rows.";

		/// <summary>The tier choices of the table, 0 to 4 (FR.16.1, FR.33.14).</summary>
		public static readonly string[] TierLabels = ["0 · local", "1 · like Haiku", "2 · like Sonnet", "3 · like Opus", "4 · like Fable"];

		// Settings > Profiles: ChatGPT (FR.33.5).
		public const string ClaudeProfilesHeader = "Claude";
		public const string ChatGptProfilesHeader = "ChatGPT";
		public const string NewChatGptTitle = "New ChatGPT profile";
		public const string SignInAgainTitle = "Sign in to ChatGPT again";
		public const string SignInPrompt = "How do you want to sign in to ChatGPT?";
		public const string BrowserChoiceId = "browser";
		public const string BrowserChoiceLabel = "Sign in with the browser";
		public const string CodeChoiceId = "code";
		public const string CodeChoiceLabel = "Sign in with a code";
		public const string BrowserWaiting = "Finish the sign-in in your browser. Tessyn waits for ChatGPT to confirm it.";
		public const string BrowserOpenFailed = "The browser could not be opened. Open this address yourself:";
		public const string CodeWaiting = "Open the address, sign in to ChatGPT and enter the code.";
		public const string DeviceCodeNote = "Sign-in with a code must be enabled in your ChatGPT security settings.";
		public const string SignedInFormat = "Signed in to ChatGPT as {0}{1}.";
		public const string SignInFailedFormat = "The ChatGPT sign-in did not complete: {0}";
		public const string SignInFailedUnknown = "no reason given";
		public const string SignedOutFormat = "{0} is signed out.";
		public const string DeleteTitle = "Delete ChatGPT profile";
		public const string ConfirmDeleteFormat = "Delete {0}? Its folder is kept with a _rem suffix; sessions pinned to it stop working.";
		public const string DeletedFormat = "{0} was deleted.";
		public const string DeletedPinnedFormat = "{0} was deleted. {1} session(s) were pinned to it.";
		public const string Copied = "Copied.";
		public const string CopyFailed = "The clipboard is busy; copy it by hand.";
		public const string SignedInText = "Signed in";
		public const string SignedOutText = "Signed out";
		public const string AuthErrorText = "Needs sign-in again";
		public const string CatalogProfileText = "model list";
		public const string PlanPrefix = "ChatGPT ";

		/// <summary>The forms of ChatGPT profile ids (tdcp-spec 10.A14): Codex Default and the stored <c>codex_N</c>.</summary>
		public const string CodexDefaultProfileId = "codex-default";
		public const string StoredProfileIdPrefix = "codex_";

		// The composer's auth notice for a ChatGPT profile (FR.33.13).
		public const string BannerBrowserOpened = "The ChatGPT sign-in opened in your browser. Finish it there. (To sign in with a code, use Settings > Profiles.)";
		public const string BannerOpenAddressFormat = "Open this address to sign in to ChatGPT: {0}";
		public const string BannerSignedInFormat = "Signed in to ChatGPT as {0}. Send again to continue.";

		// The profile flyout's section for the session's ChatGPT profile (FR.33.13).
		public const string FlyoutSessionHeader = "This session's ChatGPT profile";
		public const string FlyoutSignInAgainFormat = "Sign in again {0}";
		public const string SignInAgainAction = "Sign in again";
		public const string SignOutAction = "Sign out";
		public const string DeleteAction = "Delete";

		/// <summary>{0} the action, {1} the profile's name, {2} its id (two profiles can share an e-mail).</summary>
		public const string RowActionNameFormat = "{0}: {1} ({2})";

		/// <summary>FR.33.6: the warning next to Codex Default.</summary>
		public const string CodexDefaultWarning =
			@"Codex Default shares %USERPROFILE%\.codex with your own Codex CLI: Tessyn's GPT sessions appear in your Codex history, and a conversation open in Tessyn can't be resumed in your CLI at the same time. Prefer a Tessyn ChatGPT profile.";
	}
}
