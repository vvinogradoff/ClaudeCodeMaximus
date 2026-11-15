namespace TessynDesktop.Models.Remote;

public static partial class RemoteProtocol
{
	/// <summary>
	/// Package J3 part 2: the External MCP Server Registry and its OAuth. Loopback only, announced by the <see cref="Capability"/> flag of
	/// <c>client.hello</c>. No result carries a header value, a client secret or a token.
	/// </summary>
	public static class McpApi
	{
		public const string Capability = "mcp.registry";

		/// <summary><c>mcp.list {}</c> answers <c>{ servers: [McpServerDto] }</c> in registry order.</summary>
		public const string ListMethod = "mcp.list";

		/// <summary>
		/// <c>mcp.upsert { id?, displayName?, enabled?, transport?, url?, command?, arguments?, workingDirectory?, authType?, staticHeaderName?, staticHeaderValue?, oauth? }</c>
		/// creates (no <c>id</c>, or an unknown one) or updates a server and answers its <c>McpServerDto</c>. A member left out keeps its value; the secret
		/// members (<c>staticHeaderValue</c>, <c>oauth.clientSecret</c>) are write-only, and an empty string clears one.
		/// </summary>
		public const string UpsertMethod = "mcp.upsert";

		/// <summary><c>mcp.remove { id }</c> removes the server and its stored token and answers <c>{ removed }</c> (false when there was no such server).</summary>
		public const string RemoveMethod = "mcp.remove";

		/// <summary><c>mcp.setEnabled { id, enabled }</c> answers the <c>McpServerDto</c>.</summary>
		public const string SetEnabledMethod = "mcp.setEnabled";

		/// <summary>
		/// <c>mcp.discover { url, oauth? }</c> probes the server's well-known OAuth documents and answers <c>{ config, timedOut }</c>. The daemon stops the probe
		/// after <see cref="DiscoverTimeoutSeconds"/> and answers what it has with <c>timedOut: true</c>.
		/// </summary>
		public const string DiscoverMethod = "mcp.discover";

		/// <summary><c>mcp.authorize { id }</c> runs the interactive OAuth flow (the system browser opens on the daemon's machine) and answers <c>{ authorized, error? }</c> when it ends.</summary>
		public const string AuthorizeMethod = "mcp.authorize";

		/// <summary>Notification <c>{ id? }</c>: the registry or an authorization changed. Local stream only.</summary>
		public const string ChangedEvent = "mcp.changed";

		/// <summary>The daemon-side limit of one <see cref="DiscoverMethod"/> call.</summary>
		public const int DiscoverTimeoutSeconds = 15;
	}

	/// <summary>Package J3 part 2: the Telegram bot token and the bridge status. Loopback only.</summary>
	public static class TelegramApi
	{
		public const string Capability = "telegram.settings";

		/// <summary><c>telegram.status {}</c> answers <c>{ enabled, running, hasToken, lastError? }</c>.</summary>
		public const string StatusMethod = "telegram.status";

		/// <summary><c>telegram.setToken { token }</c> stores the token (write-only), starts the bridge when it is enabled, and answers the status.</summary>
		public const string SetTokenMethod = "telegram.setToken";

		/// <summary><c>telegram.clearToken {}</c> removes the token, stops the bridge, and answers the status.</summary>
		public const string ClearTokenMethod = "telegram.clearToken";
	}

	/// <summary>Package J3 part 2: the web relay link. Loopback only.</summary>
	public static class RelayApi
	{
		public const string Capability = "relay.settings";

		/// <summary><c>relay.status {}</c> answers <c>{ enabled, state, url?, hasToken, since, lastError? }</c>.</summary>
		public const string StatusMethod = "relay.status";

		/// <summary>
		/// <c>relay.configure { enabled?, url?, linkToken? }</c> stores the link and restarts the connector off the request thread; answers the status. The link
		/// token is write-only; it must be sent again when the URL changes to another origin.
		/// </summary>
		public const string ConfigureMethod = "relay.configure";
	}

	/// <summary>Package J3 part 2: the model catalog refresh. Loopback only.</summary>
	public static class ModelsApi
	{
		public const string Capability = "models.admin";

		/// <summary><c>models.refresh {}</c> asks the local Ollama instance again and answers <c>{ count }</c>; <see cref="CatalogChangedEvent"/> follows.</summary>
		public const string RefreshMethod = "models.refresh";

		/// <summary>Notification <c>{}</c>: the model catalog was refreshed (reload <c>catalog.get</c>). Local stream only.</summary>
		public const string CatalogChangedEvent = "catalog.changed";
	}
}
