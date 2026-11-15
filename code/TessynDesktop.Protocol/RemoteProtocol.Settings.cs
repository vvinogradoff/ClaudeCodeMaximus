using System.Collections.Generic;

namespace TessynDesktop.Models.Remote;

public static partial class RemoteProtocol
{
	/// <summary>
	/// Package J3: daemon-owned settings sections. Loopback only, announced by the <see cref="Capability"/> flag of <c>client.hello</c>. The
	/// sections hold only what the daemon owns (never the layout, theme, key bindings, secrets or the Telegram topic maps).
	/// </summary>
	public static class SettingsApi
	{
		public const string Capability = "settings";

		/// <summary><c>settings.sections {}</c> answers <c>{ sections: [{ name, version }] }</c>.</summary>
		public const string SectionsMethod = "settings.sections";

		/// <summary><c>settings.get { section }</c> answers <c>{ section, version, values }</c>.</summary>
		public const string GetMethod = "settings.get";

		/// <summary><c>settings.update { section, patch, baseVersion }</c> answers <c>{ version }</c>; a stale <c>baseVersion</c> is a <see cref="ErrorCodes.DaemonError"/> "Settings changed".</summary>
		public const string UpdateMethod = "settings.update";

		/// <summary>Notification <c>{ section, version }</c>, published after a section's values changed (also by a save in the Avalonia Settings window). Local stream only.</summary>
		public const string ChangedEvent = "settings.changed";

		public const string GeneralSection = "general";
		public const string NotificationsSection = "notifications";
		public const string TelegramSection = "telegram";

		/// <summary>
		/// The Avalonia client's own settings (theme, colours, font sizes, key bindings, screensaver), served read-only for the one-time import into
		/// another client (decision A23 Q8). <c>settings.update</c> refuses it, and it has no change notification: its version stays 1.
		/// </summary>
		public const string AvaloniaUiSection = "avalonia-ui";

		/// <summary>Every section the daemon serves, in listing order.</summary>
		public static readonly IReadOnlyList<string> Sections = [GeneralSection, NotificationsSection, TelegramSection, AvaloniaUiSection];
	}

	/// <summary>Package J3: profile administration. Loopback only; <c>profile.switch</c> (ISSUE.013) is in <see cref="LoopbackMethods"/>.</summary>
	public static class ProfilesAdmin
	{
		public const string Capability = "profiles.admin";

		/// <summary><c>profile.list {}</c> answers <c>{ profiles: [ProfileDto] }</c>: the Default profile first, then the stored ones.</summary>
		public const string ListMethod = "profile.list";

		/// <summary><c>profile.add { displayName }</c> registers a profile without logging in and answers the <c>ProfileDto</c>. Follow it with <c>profile.authenticate</c>.</summary>
		public const string AddMethod = "profile.add";

		/// <summary><c>profile.rename { profileId, displayName }</c> answers the <c>ProfileDto</c>.</summary>
		public const string RenameMethod = "profile.rename";

		/// <summary><c>profile.remove { profileId }</c> answers the removed <c>ProfileDto</c>; the config directory is preserved with a suffix.</summary>
		public const string RemoveMethod = "profile.remove";

		/// <summary><c>profile.authenticate { profileId }</c> answers <c>{ started }</c> at once; the login runs on the daemon's machine and <see cref="ChangedEvent"/> follows.</summary>
		public const string AuthenticateMethod = "profile.authenticate";

		/// <summary>Notification <c>{}</c>: the list or an auth state changed. Local stream only.</summary>
		public const string ChangedEvent = "profiles.changed";
	}

	/// <summary>Package J3: the completion sound. Loopback only.</summary>
	public static class SoundApi
	{
		public const string Capability = "sound";

		/// <summary><c>sound.list {}</c> answers <c>{ sounds: [{ file }] }</c>.</summary>
		public const string ListMethod = "sound.list";

		/// <summary><c>sound.test { file }</c> plays a .wav on the daemon's machine and answers <c>{ played }</c>.</summary>
		public const string TestMethod = "sound.test";
	}

	/// <summary>Package J3 notifications that the relay must not carry (part 2 adds <c>mcp.changed</c> and <c>catalog.changed</c>).</summary>
	public static readonly IReadOnlyList<string> AdminLocalOnlyEvents = [SettingsApi.ChangedEvent, ProfilesAdmin.ChangedEvent, McpApi.ChangedEvent, ModelsApi.CatalogChangedEvent];
}
