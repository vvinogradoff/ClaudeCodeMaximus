using System;
using TessynDesktop.Models.Remote;

namespace TessynDesktop;

/// <remarks>Created by Claude</remarks>
public static partial class Constants
{
	/// <summary>Names and timings of the remote (web client) API: the contract in .web-work/10-contract.md.</summary>
	public static class Remote
	{
		/// <summary>Version reported by <c>client.hello</c>; bumped only for a breaking wire change.</summary>
		public const int ProtocolVersion = RemoteProtocol.ProtocolVersion;

		/// <summary>JSON-RPC method names that may cross the relay (contract section 3).</summary>
		public static class Methods
		{
			public const string ClientHello = RemoteProtocol.Methods.ClientHello;
			public const string TreeSnapshot = RemoteProtocol.Methods.TreeSnapshot;
			public const string TreeWorkers = RemoteProtocol.Methods.TreeWorkers;
			public const string TreeSearch = RemoteProtocol.Methods.TreeSearch;
			public const string TreeCreateSession = RemoteProtocol.Methods.TreeCreateSession;
			public const string RecentList = RemoteProtocol.Methods.RecentList;
			public const string RecentHide = RemoteProtocol.Methods.RecentHide;
			public const string TagsSearch = RemoteProtocol.Methods.TagsSearch;
			public const string SessionGet = RemoteProtocol.Methods.SessionGet;
			public const string SessionEntries = RemoteProtocol.Methods.SessionEntries;
			public const string SessionEntry = RemoteProtocol.Methods.SessionEntry;
			public const string SessionSend = RemoteProtocol.Methods.SessionSend;
			public const string SessionStop = RemoteProtocol.Methods.SessionStop;
			public const string SessionCompact = RemoteProtocol.Methods.SessionCompact;
			public const string SessionSetFavorite = RemoteProtocol.Methods.SessionSetFavorite;
			public const string SessionSetStreaming = RemoteProtocol.Methods.SessionSetStreaming;
			public const string AttentionList = RemoteProtocol.Methods.AttentionList;
			public const string AttentionReply = RemoteProtocol.Methods.AttentionReply;
			public const string AttentionDismiss = RemoteProtocol.Methods.AttentionDismiss;
			public const string CatalogGet = RemoteProtocol.Methods.CatalogGet;
			public const string UsageGet = RemoteProtocol.Methods.UsageGet;
			public const string FileRead = RemoteProtocol.Methods.FileRead;
			public const string FilePreview = RemoteProtocol.Methods.FilePreview;
			public const string AttachmentList = RemoteProtocol.Methods.AttachmentList;
			public const string AttachmentFetch = RemoteProtocol.Methods.AttachmentFetch;
		}

		/// <summary>Notification names published on the remote event bus (contract section 4).</summary>
		public static class Events
		{
			public const string TreeChanged = RemoteProtocol.Events.TreeChanged;
			public const string SessionChanged = RemoteProtocol.Events.SessionChanged;
			public const string SessionAppended = RemoteProtocol.Events.SessionAppended;
			public const string SessionReset = RemoteProtocol.Events.SessionReset;
			public const string TurnCompleted = RemoteProtocol.Events.TurnCompleted;
			public const string AttentionChanged = RemoteProtocol.Events.AttentionChanged;
			public const string TagsChanged = RemoteProtocol.Events.TagsChanged;
			public const string UsageChanged = RemoteProtocol.Events.UsageChanged;
			public const string IndexChanged = RemoteProtocol.Events.IndexChanged;

			/// <summary>P1.F: new entries with their ids, published beside <see cref="SessionAppended"/>.</summary>
			public const string EntriesAppended = RemoteProtocol.Events.EntriesAppended;

			/// <summary>Local-stream-only notification: the client's resume point is not covered and it must re-read its state. Params: <c>{ reason, eventSeq }</c>.</summary>
			public const string Resync = RemoteProtocol.Events.Resync;
		}

		/// <summary>The event log that stamps <c>seq</c> on bus notifications and serves resume (P1.B).</summary>
		public static class EventLog
		{
			/// <summary>Most events the ring keeps.</summary>
			public const int DefaultCapacity = 5000;

			/// <summary>Events older than this are dropped from the ring.</summary>
			public static readonly TimeSpan DefaultMaxAge = TimeSpan.FromMinutes(10);

			/// <summary>Reasons carried by a <see cref="Events.Resync"/> notification.</summary>
			public static class ResyncReasons
			{
				/// <summary>The events after the client's seq are no longer (or were never) in the ring.</summary>
				public const string Gap = RemoteProtocol.ResyncReasons.Gap;

				/// <summary>The client's seq is newer than the log head, or it belongs to another daemon run.</summary>
				public const string UnknownSeq = RemoteProtocol.ResyncReasons.UnknownSeq;

				/// <summary>The stream's own queue overflowed and dropped events for a slow reader.</summary>
				public const string Overflow = RemoteProtocol.ResyncReasons.Overflow;
			}
		}

		/// <summary>The local SSE stream (<c>GET /tdcp</c>).</summary>
		public static class Sse
		{
			/// <summary>Query flag (<c>?legacy=1</c>): also emit the legacy <c>tree.changed</c> / <c>schedule.changed</c> frames.</summary>
			public const string LegacyQuery = RemoteProtocol.Sse.LegacyQuery;

			/// <summary>Optional query (<c>?runId=</c>): the run id the client's Last-Event-ID belongs to; a mismatch answers with a resync.</summary>
			public const string RunIdQuery = RemoteProtocol.Sse.RunIdQuery;

			/// <summary>Request header with the last event id a reconnecting client saw.</summary>
			public const string LastEventIdHeader = RemoteProtocol.Sse.LastEventIdHeader;

			/// <summary>Frames buffered per stream before the oldest are dropped (and a resync is signalled).</summary>
			public const int ChannelCapacity = 1000;
		}

		/// <summary>Feature flags announced by <c>client.hello</c>; a client enables a feature only when it is listed.</summary>
		public static class Capabilities
		{
			/// <summary>The local SSE stream carries the bus notifications with ids.</summary>
			public const string EventsLocal = RemoteProtocol.Capabilities.EventsLocal;

			/// <summary>The local SSE stream honours Last-Event-ID and signals gaps with <see cref="Events.Resync"/>.</summary>
			public const string EventsResume = RemoteProtocol.Capabilities.EventsResume;

			/// <summary>P1.F: entry ids, <c>fileId</c>/<c>seq</c> and <c>entries.appended</c>.</summary>
			public const string EntriesIds = RemoteProtocol.Capabilities.EntriesIds;
		}

		/// <summary>Oldest client protocol version the daemon still serves.</summary>
		public const int MinProtocolVersion = RemoteProtocol.MinProtocolVersion;

		/// <summary>
		/// How long <c>client.hello</c> reuses its read-only Codex status (FR.33.17, tdcp-spec 10.A14). Reading it runs <c>codex --version</c>, and the
		/// web calls <c>client.hello</c> on every resync, so the reading is shared; a changed Codex path shows within this time.
		/// </summary>
		public static readonly TimeSpan HelloCodexStatusLifetime = TimeSpan.FromMinutes(1);

		/// <summary>Daemon discovery file (<c>daemon.json</c>) and the local token file.</summary>
		public static class Discovery
		{
			public const string FileName = RemoteProtocol.Discovery.FileName;
			public const string TokenFileName = RemoteProtocol.Discovery.TokenFileName;
		}

		/// <summary>Debounce windows of the notification publisher (contract section 4).</summary>
		public static class Timings
		{
			public static readonly TimeSpan TreeChangedWindow = TimeSpan.FromMilliseconds(300);
			public static readonly TimeSpan SessionChangedWindow = TimeSpan.FromMilliseconds(250);
			public static readonly TimeSpan SessionAppendedWindow = TimeSpan.FromMilliseconds(250);
			public static readonly TimeSpan AttentionChangedWindow = TimeSpan.FromMilliseconds(250);
			public static readonly TimeSpan TagsChangedWindow = TimeSpan.FromMilliseconds(250);
			public static readonly TimeSpan IndexChangedWindow = TimeSpan.FromSeconds(2);

			/// <summary>Window for notifications that carry no debounce of their own (turn.completed, session.reset): run the factory right away, off the caller's thread.</summary>
			public static readonly TimeSpan Immediate = TimeSpan.Zero;
		}

		/// <summary>Relay link (contract section 6) — consumed by the relay connector work package.</summary>
		public static class Link
		{
			public const string StreamPath = "/link/stream";
			public const string ReplyPath = "/link/reply";
			public const string EventsPath = "/link/events";
			public const string BlobPath = "/link/blob/";
			public const string DaemonHeader = "X-Tessyn-Daemon";
			public const string ProtocolHeader = "X-Tessyn-Protocol";
			public const int DispatchConcurrency = 8;

			/// <summary>File name of the DPAPI-protected relay config under %AppData%\TessynDesktop (contract section 6).</summary>
			public const string ConfigFileName = "web-relay.dat";

			/// <summary>Protocol version sent in <see cref="ProtocolHeader"/>.</summary>
			public const string ProtocolHeaderValue = "1";

			/// <summary>Shown as the connector's error when the server answers 401.</summary>
			public const string TokenRejectedMessage = "Link token rejected";

			/// <summary>How long Stop() waits for the connector loop to finish.</summary>
			public static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(5);

			/// <summary>Per-request timeout of the reply and event POSTs.</summary>
			public static readonly TimeSpan PostTimeout = TimeSpan.FromSeconds(30);

			/// <summary>Capacity of the outbound event queue; the oldest events are dropped beyond it.</summary>
			public const int EventQueueCapacity = 5000;
			public static readonly TimeSpan PingInterval = TimeSpan.FromSeconds(15);
			public static readonly TimeSpan DeadLinkTimeout = TimeSpan.FromSeconds(45);
			public static readonly TimeSpan EventBatchWindow = TimeSpan.FromMilliseconds(250);
			public static readonly TimeSpan BackoffResetAfter = TimeSpan.FromSeconds(60);
			public static readonly TimeSpan[] ReconnectBackoff =
			[
				TimeSpan.FromSeconds(1),
				TimeSpan.FromSeconds(2),
				TimeSpan.FromSeconds(5),
				TimeSpan.FromSeconds(10),
				TimeSpan.FromSeconds(30),
			];
		}

		/// <summary>Limits of the session-content methods (contract sections 3.10 and 3.20).</summary>
		public static class Limits
		{
			public const int EntryContentMaxChars = 32_768;
			public const int EntriesDefaultLimit = 50;
			public const int EntriesMaxLimit = 200;

			// P1.F / P1.G (grouped to keep merges easy).

			/// <summary><c>entries.appended</c> carries the entries inline only up to this many of them ...</summary>
			public const int EntriesAppendedInlineMaxEntries = 50;

			/// <summary>... and only while their UTF-8 size (content included) stays within this many bytes; otherwise ids only.</summary>
			public const int EntriesAppendedInlineMaxBytes = 65_536;

			/// <summary>Most NodeIds one <c>session.headers</c> call accepts.</summary>
			public const int HeadersMaxNodeIds = 500;
		}

		/// <summary>Per-model effort values offered by <c>catalog.get</c>; the first is "no flag".</summary>
		public static readonly string[] Efforts = ["default", "max", "high", "medium", "low"];

		/// <summary>Id of the "no explicit choice" model and profile entries of the catalog.</summary>
		public const string DefaultOptionId = "default";

		/// <summary>Usage fetch (contract section 3.21).</summary>
		public static class Usage
		{
			/// <summary>How long a per-profile fetch result is served without asking the API again.</summary>
			public static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

			/// <summary>How long after a failed fetch (no token, network or API error) the next one may be made.</summary>
			public static readonly TimeSpan FailureRetry = TimeSpan.FromMinutes(1);

			/// <summary>How long a usage.get waits for a fetch before answering "pending" (the fetch keeps running).</summary>
			public static readonly TimeSpan FetchWait = TimeSpan.FromSeconds(8);

			/// <summary>Upper bound of one fetch of the usage endpoint.</summary>
			public static readonly TimeSpan FetchTimeout = TimeSpan.FromSeconds(20);

			/// <summary>Window in which usage.changed notifications of one profile are merged.</summary>
			public static readonly TimeSpan ChangedWindow = TimeSpan.FromMilliseconds(250);

			/// <summary>The labels and lengths of a Claude profile's two windows on the wire (tdcp-spec 10.A14).</summary>
			public const string FiveHourLabel = "5h";
			public const string SevenDayLabel = "7d";
			public const int FiveHourMinutes = 5 * 60;
			public const int SevenDayMinutes = 7 * 24 * 60;
		}

		/// <summary>Profile labels (contract sections 3.12 and 3.20).</summary>
		public static class Profiles
		{
			/// <summary>Prefix of the Default profile's label; the desktop stamps "[Sys] &lt;email&gt;" on USER entries.</summary>
			public const string SystemPrefix = "[Sys] ";

			/// <summary>Shown for the Default profile while its account e-mail is unknown.</summary>
			public const string FallbackName = "Default";

			/// <summary>How long a resolved Default label is reused.</summary>
			public static readonly TimeSpan LabelCacheDuration = TimeSpan.FromMinutes(10);

			/// <summary>How long a failed resolution is not retried.</summary>
			public static readonly TimeSpan LabelFailureCacheDuration = TimeSpan.FromMinutes(1);

			/// <summary>A headless turn never waits longer than this for the account e-mail.</summary>
			public static readonly TimeSpan LabelResolveTimeout = TimeSpan.FromSeconds(5);
		}

		/// <summary>Parsed-entry cache of session.entries.</summary>
		public static class EntryCache
		{
			/// <summary>Most session files whose parsed entries are kept in memory.</summary>
			public const int MaxFiles = 16;
		}

		/// <summary>How much of a session file's end the activity service parses (contract section 3, SessionActivityService).</summary>
		public const int ActivityTailBytes = 64 * 1024;

		/// <summary>Largest tail a cold activity read may parse when the first tail holds no USER entry; beyond it the last prompt is reported as unknown.</summary>
		public const int ActivityColdParseMaxBytes = 4 * 1024 * 1024;
	}
}
