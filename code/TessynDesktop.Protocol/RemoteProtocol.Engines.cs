namespace TessynDesktop.Models.Remote;

public static partial class RemoteProtocol
{
	/// <summary>
	/// Session engines on the wire (FR.33, tdcp-spec 10.A14): the ids carried by <c>catalog.get</c> models and profiles, <c>SessionStateDto.engine</c>,
	/// <c>usage.get</c> and <c>client.hello</c>'s <c>engines</c>. Additive: a member an older daemon does not send reads as <see cref="Claude"/>.
	/// </summary>
	public static class Engines
	{
		/// <summary>The Claude Code CLI; also what an absent engine means.</summary>
		public const string Claude = "claude";

		/// <summary>The Codex CLI's app-server (GPT models on the ChatGPT plan).</summary>
		public const string Codex = "codex";

		/// <summary>Capability of <c>client.hello</c>: the result carries <c>engines</c>, the engines whose CLI was found. On both transports.</summary>
		public const string Capability = "engines";
	}

	/// <summary>
	/// ChatGPT profile administration (FR.33.5, FR.33.17, tdcp-spec 10.A14). Loopback only, the J3 pattern: the relay answers <c>-32601</c> and never
	/// carries <see cref="LoginCompletedEvent"/>. Delete is <c>profile.remove</c> with a ChatGPT profile id; the list is <c>profile.list</c>'s
	/// <c>codexProfiles</c>. Announced by the capability <see cref="Capability"/>.
	/// </summary>
	public static class CodexProfilesAdmin
	{
		public const string Capability = "profiles.codex";

		/// <summary>
		/// <c>profiles.codexLogin { mode: "browser" | "deviceCode", profileId? }</c> answers <c>{ profileId, loginId, mode, authUrl?, verificationUrl?, userCode? }</c>
		/// at once; <see cref="LoginCompletedEvent"/> follows when the sign-in ends. Without <c>profileId</c> the sign-in creates the next <c>codex_N</c>
		/// (added only on success); with it, it signs that profile in again.
		/// </summary>
		public const string LoginMethod = "profiles.codexLogin";

		/// <summary><c>profiles.codexLoginCancel { loginId }</c> answers <c>{ cancelled }</c>; the sign-in then ends with a failed <see cref="LoginCompletedEvent"/>.</summary>
		public const string LoginCancelMethod = "profiles.codexLoginCancel";

		/// <summary><c>profiles.codexLogout { profileId }</c> signs the profile out (<c>account/logout</c>) and answers its <c>CodexProfileDto</c>; it stays listed.</summary>
		public const string LogoutMethod = "profiles.codexLogout";

		/// <summary>Notification <c>{ loginId, profileId, success, error?, email?, plan? }</c>: a sign-in ended. Local stream only.</summary>
		public const string LoginCompletedEvent = "profiles.codexLoginCompleted";

		/// <summary>The browser sign-in: the client opens <c>authUrl</c> in the system browser.</summary>
		public const string BrowserMode = "browser";

		/// <summary>The device-code sign-in: the client shows <c>userCode</c> and <c>verificationUrl</c>.</summary>
		public const string DeviceCodeMode = "deviceCode";
	}
}
