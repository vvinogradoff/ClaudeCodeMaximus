using System;

namespace TessynDesktop;

/// <remarks>Created by Claude</remarks>
public static partial class Constants
{
	/// <summary>Names and timings of the remote (web client) API: the contract in .web-work/10-contract.md.</summary>
	public static class Remote
	{
		/// <summary>Version reported by <c>client.hello</c>; bumped only for a breaking wire change.</summary>
		public const int ProtocolVersion = 1;

		/// <summary>JSON-RPC method names that may cross the relay (contract section 3).</summary>
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

		/// <summary>Notification names published on the remote event bus (contract section 4).</summary>
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
			public const string DaemonHeader = "X-Tessyn-Daemon";
			public const string ProtocolHeader = "X-Tessyn-Protocol";
			public const int DispatchConcurrency = 8;
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
	}
}
