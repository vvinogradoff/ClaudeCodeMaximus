namespace TessynDesktop;

/// <remarks>Created by Claude</remarks>
public static partial class Constants
{
	/// <summary>Limits and messages of the MCP registry, Telegram, relay and model methods (package J3 part 2).</summary>
	public static class IntegrationsApi
	{
		/// <summary>The daemon-side limit of one <c>mcp.discover</c> call (programme history: an unbounded discovery once hung Settings).</summary>
		public static readonly System.TimeSpan McpDiscoverTimeout = System.TimeSpan.FromSeconds(Models.Remote.RemoteProtocol.McpApi.DiscoverTimeoutSeconds);

		/// <summary>The daemon-side limit of one <c>models.refresh</c> call.</summary>
		public static readonly System.TimeSpan ModelsRefreshTimeout = System.TimeSpan.FromSeconds(30);

		/// <summary>Most characters of a URL (an MCP server URL may carry a query).</summary>
		public const int MaxUrlLength = 2048;

		/// <summary>Most characters of a command line, a header value or an OAuth client secret.</summary>
		public const int MaxSecretLength = 4096;

		/// <summary>Most characters of an MCP server id (it is also a file name).</summary>
		public const int MaxMcpIdLength = 64;

		/// <summary>Most characters of an error text that a result carries.</summary>
		public const int MaxErrorLength = 300;

		/// <summary>Most MCP servers the registry holds.</summary>
		public const int MaxMcpServers = 200;

		/// <summary>The shape of a Telegram bot token: <c>123456:ABC-DEF...</c>. It goes into a URL path, so nothing else is accepted.</summary>
		public const string TelegramTokenPattern = @"^[0-9]{1,20}:[A-Za-z0-9_-]{10,120}$";

		/// <summary>The characters of a header name (RFC 9110 token).</summary>
		public const string HeaderNamePattern = @"^[!#$%&'*+\-.^_`|~0-9A-Za-z]{1,100}$";

		/// <summary>The characters of an MCP server id.</summary>
		public const string McpIdPattern = @"^[A-Za-z0-9_-]{1,64}$";
	}
}
