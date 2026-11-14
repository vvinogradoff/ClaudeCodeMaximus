namespace TessynDesktop.Models.Remote;

/// <summary>
/// The wire-level vocabulary shared by the daemon, the relay and every client: method names, notification names, error codes,
/// the token header, the protocol version and the capability names. Contract: <c>.web-work/10-contract.md</c>.
/// </summary>
/// <remarks>Created by Claude</remarks>
public static class RemoteProtocol
{
	/// <summary>Version reported by <c>client.hello</c>; bumped only for a breaking wire change.</summary>
	public const int ProtocolVersion = 1;

	/// <summary>Oldest client protocol version the daemon still serves.</summary>
	public const int MinProtocolVersion = 1;

	/// <summary>Request header carrying the local token.</summary>
	public const string TokenHeader = "X-CMX-Token";

	/// <summary>Path of the TDCP endpoint on the daemon's listener.</summary>
	public const string EndpointPath = "/tdcp";

	/// <summary>JSON-RPC method names (contract section 3).</summary>
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

	/// <summary>Notification names (contract section 4).</summary>
	public static class Events
	{
		public const string TreeChanged = "tree.changed";
		public const string SessionChanged = "session.changed";
		public const string SessionAppended = "session.appended";
		public const string SessionReset = "session.reset";
		public const string TurnCompleted = "turn.completed";
		public const string AttentionChanged = "attention.changed";
		public const string TagsChanged = "tags.changed";
		public const string UsageChanged = "usage.changed";
		public const string IndexChanged = "index.changed";

		/// <summary>Local-stream-only notification: the client's resume point is not covered and it must re-read its state. Params: <c>{ reason, eventSeq }</c>.</summary>
		public const string Resync = "resync";
	}

	/// <summary>Reasons carried by a <see cref="Events.Resync"/> notification.</summary>
	public static class ResyncReasons
	{
		/// <summary>The events after the client's seq are no longer (or were never) in the ring.</summary>
		public const string Gap = "gap";

		/// <summary>The client's seq is newer than the log head, or it belongs to another daemon run.</summary>
		public const string UnknownSeq = "unknown-seq";

		/// <summary>The stream's own queue overflowed and dropped events for a slow reader.</summary>
		public const string Overflow = "overflow";
	}

	/// <summary>Feature flags announced by <c>client.hello</c>; a client enables a feature only when it is listed.</summary>
	public static class Capabilities
	{
		/// <summary>The local SSE stream carries the bus notifications with ids.</summary>
		public const string EventsLocal = "events.local";

		/// <summary>The local SSE stream honours Last-Event-ID and signals gaps with <see cref="Events.Resync"/>.</summary>
		public const string EventsResume = "events.resume";
	}

	/// <summary>JSON-RPC 2.0 error codes used on the wire.</summary>
	public static class ErrorCodes
	{
		public const int ParseError = -32700;
		public const int InvalidRequest = -32600;
		public const int MethodNotFound = -32601;
		public const int InvalidParams = -32602;
		public const int InternalError = -32603;

		/// <summary>A daemon-level failure whose message is safe to show.</summary>
		public const int DaemonError = -32000;

		/// <summary>Relay only: the daemon behind the relay is offline.</summary>
		public const int DaemonOffline = -32001;

		/// <summary>Relay only: the daemon did not answer in time.</summary>
		public const int DaemonTimeout = -32002;
	}

	/// <summary>The local SSE stream (<c>GET /tdcp</c>).</summary>
	public static class Sse
	{
		/// <summary>Query flag (<c>?legacy=1</c>): also emit the legacy <c>tree.changed</c> / <c>schedule.changed</c> frames.</summary>
		public const string LegacyQuery = "legacy";

		/// <summary>Optional query (<c>?runId=</c>): the run id the client's Last-Event-ID belongs to; a mismatch answers with a resync.</summary>
		public const string RunIdQuery = "runId";

		/// <summary>Request header with the last event id a reconnecting client saw.</summary>
		public const string LastEventIdHeader = "Last-Event-ID";
	}

	/// <summary>Daemon discovery file and the local token file, both under <c>%AppData%\TessynDesktop</c>.</summary>
	public static class Discovery
	{
		public const string FileName = "daemon.json";
		public const string TokenFileName = "tdcp-token.dat";
	}
}
