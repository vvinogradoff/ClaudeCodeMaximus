namespace Tessyn.Web;

/// <remarks>Created by Claude</remarks>
public static class Constants
{
	public static class Config
	{
		public const string Section = "TessynWeb";
		public const string ExtraConfigVariable = "TESSYN_WEB_CONFIG";
		public const string ServiceName = "Tessyn.Web";
	}

	public static class Cli
	{
		public const string HashPasswordVerb = "hash-password";
		public const string NewLinkTokenVerb = "new-link-token";
		public const string TokenLabel = "token: ";
		public const string TokenSha256Label = "sha256: ";
		public const string PasswordPrompt = "Enter password (read from stdin): ";
		public const string EmptyPasswordError = "No password supplied on stdin.";
	}

	public static class Paths
	{
		public const string ApiPrefix = "/api";
		public const string LinkPrefix = "/link";
		public const string Health = "/healthz";
		public const string Login = "/api/auth/login";
		public const string Logout = "/api/auth/logout";
		public const string Me = "/api/auth/me";
		public const string Rpc = "/api/rpc";
		public const string Events = "/api/events";
		public const string LinkStream = "/link/stream";
		public const string LinkReply = "/link/reply";
		public const string LinkEvents = "/link/events";
		public const string IndexFile = "index.html";
		public const string AssetsPrefix = "/assets/";
	}

	public static class Headers
	{
		public const string Client = "X-Tessyn-Client";
		public const string ClientValue = "web";
		public const string Daemon = "X-Tessyn-Daemon";
		public const string Protocol = "X-Tessyn-Protocol";
		public const string AccelBuffering = "X-Accel-Buffering";
		public const string LastEventId = "Last-Event-ID";
		public const string ContentSecurityPolicy = "Content-Security-Policy";
		public const string ContentTypeOptions = "X-Content-Type-Options";
		public const string ReferrerPolicy = "Referrer-Policy";
		public const string FrameOptions = "X-Frame-Options";
		public const string BearerPrefix = "Bearer ";
	}

	public static class HeaderValues
	{
		public const string ContentSecurityPolicy = "default-src 'self'; img-src 'self' data:; style-src 'self' 'unsafe-inline'; connect-src 'self'; frame-ancestors 'none'";
		public const string NoSniff = "nosniff";
		public const string NoReferrer = "no-referrer";
		public const string FrameDeny = "DENY";
		public const string NoStore = "no-store";
		public const string NoCache = "no-cache";
		public const string AccelBufferingOff = "no";
		public const string ImmutableAssets = "public, max-age=31536000, immutable";
		public const string ProtocolVersion = "1";
	}

	public static class ContentTypes
	{
		public const string EventStream = "text/event-stream";
		public const string Json = "application/json";
		public const string Html = "text/html; charset=utf-8";
	}

	public static class Auth
	{
		public const string CookieName = ".Tessyn.Auth";
		public const string CookieScheme = "TessynCookie";
		public const string InvalidCredentialsMessage = "Invalid email or password";
		public const string ThrottledMessage = "Too many failed attempts";
		public const string RetryAfterHeader = "Retry-After";
		public const string MissingClientHeaderMessage = "Missing X-Tessyn-Client header";
		public const string UnknownClientIp = "unknown";
	}

	public static class Pbkdf2
	{
		public const string Prefix = "pbkdf2-sha256";
		public const char Separator = '$';
		public const int Iterations = 600_000;
		public const int SaltBytes = 16;
		public const int HashBytes = 32;
		public const int MinIterations = 1;
		public const int MaxIterations = 10_000_000;
		public const int PartCount = 4;
	}

	public static class Defaults
	{
		public const int CookieDays = 30;
		public const double RequestTimeoutSeconds = 30;
		public const double LongRequestTimeoutSeconds = 60;
		public const double PingSeconds = 15;
		public const int RingBufferEvents = 1000;
		public const double RingBufferMinutes = 10;
		public const int PerIpFailures = 5;
		public const double PerIpWindowMinutes = 15;
		public const int GlobalFailures = 20;
		public const double GlobalWindowMinutes = 60;
	}

	public static class Limits
	{
		public const int SubscriberQueue = 2000;
		public const int MaxBodyBytes = 10 * 1024 * 1024;
		public const int TokenBytes = 32;
		public const int Sha256HexLength = 64;
		public const int DaemonNameMaxLength = 64;
	}

	public static class JsonRpc
	{
		public const string Version = "2.0";
		public const int ParseError = -32700;
		public const int InvalidRequest = -32600;
		public const int MethodNotFound = -32601;
		public const int InvalidParams = -32602;
		public const int DaemonOffline = -32001;
		public const int DaemonTimeout = -32002;
		public const string ParseErrorMessage = "Parse error";
		public const string InvalidRequestMessage = "Invalid request";
		public const string MethodNotFoundMessage = "Method not found";
		public const string InvalidParamsMessage = "Invalid params";
		public const string DaemonOfflineMessage = "Daemon offline";
		public const string DaemonTimeoutMessage = "Daemon timeout";
	}

	public static class Notifications
	{
		public const string LinkStatus = "link.status";
		public const string Resync = "resync";
	}

	public static class Remote
	{
		public static class Methods
		{
			public const string ClientHello = "client.hello";
			public const string TreeSnapshot = "tree.snapshot";
			public const string TreeWorkers = "tree.workers";
			public const string TreeSearch = "tree.search";
			public const string TreeCreateSession = "tree.createSession";
			public const string RecentList = "recent.list";
			public const string RecentHide = "recent.hide";
			public const string TagsSearch = "tags.search";
			public const string SessionGet = "session.get";
			public const string SessionEntries = "session.entries";
			public const string SessionEntry = "session.entry";
			public const string SessionSend = "session.send";
			public const string SessionStop = "session.stop";
			public const string SessionCompact = "session.compact";
			public const string SessionSetFavorite = "session.setFavorite";
			public const string SessionSetStreaming = "session.setStreaming";
			public const string AttentionList = "attention.list";
			public const string AttentionReply = "attention.reply";
			public const string AttentionDismiss = "attention.dismiss";
			public const string CatalogGet = "catalog.get";
			public const string UsageGet = "usage.get";
		}
	}

	public static class SpaFallback
	{
		public const string NotBuiltHtml = "<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><title>Tessyn Web</title></head><body style=\"font-family:sans-serif;margin:2rem\"><h1>Tessyn Web</h1><p>The web client has not been built yet (wwwroot/index.html is missing).</p></body></html>";
	}
}
