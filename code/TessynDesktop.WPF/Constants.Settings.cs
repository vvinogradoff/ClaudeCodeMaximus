namespace TessynDesktop.WPF;

/// <summary>Texts, ids and limits of the Settings window (W6a, W6b).</summary>
/// <remarks>Created by Claude</remarks>
public static partial class Constants
{
	public static class Settings
	{
		public const string OpenCommandId = "app.openSettings";
		/// <summary>Name of the client settings section that remembers the one-time Avalonia import.</summary>
		public const string AvaloniaImportSection = "avaloniaImport";
		public const string OpenCommandTitle = "Settings…";
		public const string OpenCommandGesture = "Ctrl+OemComma";
		/// <summary>Position of the entry in the hamburger menu (after Import).</summary>
		public const int OpenButtonOrder = 900;

		public const string WindowTitle = "Settings";
		public const string NotConnected = "Tessyn is not connected to the daemon yet; open Settings again when the connection is up.";
		public const string Unavailable = "This daemon does not offer this part of Settings (it does not announce '{0}').";

		public const string StartBackgroundDaemonHint = "Off (the default): when no daemon runs, this window waits for the Tessyn (Avalonia) app and connects as soon as it is up, so the Avalonia window can be started after this one. On: this window starts the daemon in the background itself. Takes effect at once.";

		public const string Saved = "Saved.";
		public const string NothingToSave = "No changes to save.";
		public const string SaveFailedFormat = "Not saved: {0}";
		public const string ConflictFormat = "'{0}' was changed elsewhere. Your edits are kept on top of the new values: press Save again to apply them, or Reload to drop them.";
		public const string ChangedElsewhereFormat = "'{0}' was changed elsewhere. Reload to see the new values.";
		public const string RestartHint = "Takes effect the next time the daemon starts.";

		/// <summary>The settings error the daemon answers for a stale <c>baseVersion</c>.</summary>
		public const string StaleVersionMessage = "Settings changed";

		public const int MaxMediaHosts = 200;
		public const int MaxThrottleSeconds = 86400;

		public const string ProxyHint = "When set, routes Claude CLI traffic through this proxy and disables TLS verification on the spawned process.";
		public const string FileAccessReferencedOnly = "Referenced files only";
		public const string FileAccessWorkingDirectory = "Anything in the working directory";
		public const string ReadOnlyHint = "Shown for information: the daemon does not let a client change this over the API. Edit it in appsettings.json.";

		public const string ProfilesWindowHint = "Profiles are shared by every client. Add, rename, remove and sign in again here or in the status-bar flyout.";
		public const string ModelsRefreshed = "The model catalog now has {0} model(s).";

		public const string NotAnInteger = "'{0}' must be a whole number.";
		public const string OutOfRange = "'{0}' must be between {1} and {2}.";
		public const string NotAnId = "'{0}' is not a valid user id.";
		public const string TooManyHosts = "At most {0} media hosts.";

		public const string ThemeSystem = "Follow Windows";
		public const string ThemeLight = "Light";
		public const string ThemeDark = "Dark";

		public const double MinFontSize = 8;
		public const double MaxFontSize = 40;
		public const double MinLineSpacing = 1.0;
		public const double MaxLineSpacing = 1.8;
		public const string LineSpacingInvalid = "Line spacing must be a number between {0} and {1}.";
		public const int MinScreensaverSeconds = 1;
		public const int MaxScreensaverSeconds = 86400;
		public const string FontSizeInvalid = "Font sizes must be numbers between {0} and {1}.";
		public const string ScreensaverInvalid = "The screensaver times must be whole seconds (the idle time may be 0 = off, the interval at least {0}).";
		public const string ScreensaverFolderTitle = "Choose the screensaver images folder";

		public const string GestureInvalidFormat = "'{0}' is not a valid key combination for {1}.";
		public const string GestureConflictFormat = "{0} also uses this combination.";

		public const string TelegramTokenSet = "A bot token is stored.";
		public const string TelegramTokenNotSet = "No bot token stored.";
		public const string TelegramTokenEmpty = "Paste the bot token first.";
		public const string TelegramRunning = "The bridge is running.";
		public const string TelegramStopped = "The bridge is not running.";
		public const string TelegramIdsHint = "Comma-separated, for example 123456789, 987654321.";

		public const string RelayTokenSet = "A link token is stored.";
		public const string RelayTokenNotSet = "No link token stored.";
		public const string RelayStateFormat = "State: {0}";
		public const string RelayStateSinceFormat = "State: {0} since {1:HH:mm:ss}";
		public const string RelaySaved = "Saved. The connection restarts in the background; press Refresh to see its state.";
		public const string RelayTokenNeeded = "Paste the link token before switching web access on.";

		public const string McpAddTitle = "Add MCP server";
		public const string McpEditTitle = "Edit MCP server";
		public const string McpRemoveTitle = "Remove MCP server";
		public const string McpConfirmRemove = "Remove '{0}' and its stored authorization?";
		public const string McpAuthorizing = "Authorizing '{0}': finish the sign-in in the browser on the daemon's machine…";
		public const string McpAuthorized = "'{0}' is authorized.";
		public const string McpAuthorizeFailed = "Authorization of '{0}' failed: {1}";
		public const string McpDiscovering = "Looking for the OAuth endpoints…";
		public const string McpDiscoveryTimedOut = "Discovery took too long; enter the endpoints by hand or try again.";
		public const string McpDiscoveryFound = "OAuth endpoints found.";
		public const string McpDiscoveryNothing = "No OAuth endpoints found at this address.";
		public const string McpNameRequired = "A display name is required.";
		public const string McpUrlRequired = "An http(s) URL is required.";
		public const string McpCommandRequired = "A command is required.";
		public const string McpHeaderNameRequired = "A header name is required.";
		public const string McpStdioWarning = "A stdio server is a command Claude starts for every session, as you. Only add commands you trust.";
		public const string McpSecretSet = "A value is stored. Leave the field empty to keep it, type a new one to replace it.";
		public const string McpSecretNotSet = "Nothing stored.";
		public const string McpUrlQueryHidden = "The stored URL has a query string (it is not shown); the query is kept unless you change the URL.";
		public const string McpArgumentsHidden = "Arguments are stored (not shown); the stored arguments are kept unless you type new ones.";

		public const string ImportTitle = "Import from the Avalonia client";
		public const string ImportPrompt = "Copy the theme, font sizes, key bindings and screensaver of the Avalonia client into this client? Your current values here are replaced.";
		public const string ImportConfirm = "Import";
		public const string ImportDone = "Imported {0}.";
		public const string ImportSkippedFormat = "Not imported (no matching command here): {0}.";
		public const string ImportFailedFormat = "Import failed: {0}";
		public const string ImportNothing = "The Avalonia client has no settings to import.";
		public const string ImportOffer = "Settings of the Avalonia client are available. Use 'Import from Avalonia' to carry them over once.";
	}
}
