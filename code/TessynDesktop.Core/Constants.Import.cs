using System;

namespace TessynDesktop;

/// <summary>Limits and refusal texts of the J4 methods: import, global search, schedules, the attention list and the daemon health. One file per package keeps the shared constants file free of merge conflicts.</summary>
/// <remarks>Created by Claude</remarks>
public static partial class Constants
{
	public static class Import
	{
		public const int ListDefaultLimit = 50;
		public const int ListMaxLimit = 200;

		/// <summary>The most ids one <c>import.titles</c> call accepts.</summary>
		public const int TitlesMaxIds = 200;

		/// <summary>A session that got no title is not tried again for this many minutes.</summary>
		public const int TitleRetryMinutes = 5;

		/// <summary>The most sessions remembered as "no title".</summary>
		public const int TitleGaveUpMax = 5000;

		/// <summary>The most sessions one <c>import.run</c> call accepts.</summary>
		public const int RunMaxItems = 100;

		/// <summary>The most messages of the index a global <c>import.list</c> looks at.</summary>
		public const int GlobalSearchMessages = 300;

		public const int FirstPromptMaxChars = 300;
		public const int NameMaxChars = 200;
		public const int QueryMaxChars = 500;
		public const int ExternalIdMaxChars = 128;

		/// <summary>The name of a session that has neither a title nor a first prompt.</summary>
		public const string DefaultSessionName = "Imported Session";

		/// <summary>A first prompt used as a session name is cut to this many characters, three of them the ellipsis.</summary>
		public const int PromptNameMaxChars = 60;

		public const string Ellipsis = "...";

		/// <summary>The line of a JSONL file that names the folder the session ran in.</summary>
		public const string JsonlWorkingDirectoryKey = "cwd";

		/// <summary>How many lines of a JSONL file are read to find <see cref="JsonlWorkingDirectoryKey"/>.</summary>
		public const int JsonlWorkingDirectoryScanLines = 60;

		public const string TargetNotFound      = "Import target not found";
		public const string DirectoryNotFound   = "Directory not found";
		public const string SourceRequired      = "'directoryId' or 'global' is required";
		public const string QueryRequired       = "'query' is required for a global listing";
		public const string QueryTooLong        = "'query' is too long";
		public const string TooManyIds          = "Too many ids";
		public const string TooManyItems        = "Too many items";
		public const string InvalidExternalId   = "'externalId' is not a Claude session id";
		public const string NameTooLong         = "'name' is too long";
		public const string SessionNotFound     = "Session not found";
		public const string AlreadyImported     = "Already imported";
		public const string SessionEmpty        = "The session has no messages";
		public const string ImportFailed        = "The session could not be imported";
		public const string DuplicateRequest    = "Requested twice";
	}

	public static class GlobalSearch
	{
		public const int DefaultLimit = 50;
		public const int MaxLimit = 100;

		/// <summary>The search looks at this many of the best-ranked messages and reports their sessions.</summary>
		public const int FetchMessages = 300;

		public const int SnippetMaxChars = 240;
		public const int QueryMaxChars = 500;
		public const int SlugMaxChars = 300;

		public const string QueryRequired = "'query' must not be blank";
		public const string QueryTooLong  = "'query' is too long";
		public const string BadSlug       = "'projectSlug' is not a project slug";
	}

	public static class ScheduleApi
	{
		public const int PromptMaxChars = 100_000;
		public const int NoteMaxChars = 500;
		public const int CronMaxChars = 200;
		public const int MaxFiresLimit = 10_000;

		/// <summary>The most schedules one session may have (mailbox deliveries do not count).</summary>
		public const int MaxPerSession = 100;

		/// <summary>The most schedules in all.</summary>
		public const int MaxTotal = 1000;

		/// <summary>A cron schedule must not fire more often than this (fire times are whole minutes, so every minute is the refused case).</summary>
		public const int MinCronIntervalMinutes = 2;

		/// <summary>How many consecutive fire gaps of a cron expression are checked.</summary>
		public const int CronGapsChecked = 3;

		/// <summary>Window in which schedule.changed notifications are merged.</summary>
		public static readonly TimeSpan ChangedWindow = TimeSpan.FromMilliseconds(250);

		/// <summary>A one-shot schedule may not be set further ahead than this.</summary>
		public const int MaxYearsAhead = 10;

		/// <summary>A one-shot schedule in the past is refused beyond this slack (clock drift between client and daemon).</summary>
		public const int PastSlackSeconds = 60;

		public const string NodeNotFound      = "Session not found";
		public const string PromptRequired    = "'prompt' must not be blank";
		public const string PromptTooLong     = "'prompt' is too long";
		public const string NoteTooLong       = "'note' is too long";
		public const string UnknownKind       = "'kind' must be delay, at or cron";
		public const string FireAtRequired    = "'fireAtUtc' is required for delay and at";
		public const string FireAtPast        = "'fireAtUtc' is in the past";
		public const string FireAtTooFar      = "'fireAtUtc' is too far ahead";
		public const string CronRequired      = "'cron' is required for a cron schedule";
		public const string CronInvalid       = "'cron' is not a valid cron expression";
		public const string CronNotAllowed    = "'cron' is only for a cron schedule";
		public const string FireAtNotAllowed  = "'fireAtUtc' is not for a cron schedule";
		public const string UnknownMissedFire = "'missedFire' must be fireOnce or skip";
		public const string BadMaxFires       = "'maxFires' is out of range";
		public const string TooManyForSession = "This session has too many schedules; remove some first";
		public const string TooManyInTotal    = "There are too many schedules; remove some first";
		public const string CronTooFrequent   = "'cron' fires too often; the interval must be at least 2 minutes";
	}

	public static class AttentionAll
	{
		public const int DefaultLimit = 100;
		public const int MaxLimit = 200;

		/// <summary>Joins the node id and the item id in the paging cursor (an item id such as "A12" is only unique inside its session).</summary>
		public const char CursorSeparator = '/';

		public const string BadStatus = "'status' is not an attention status";
		public const string BadKind   = "'kinds' holds an unknown kind";
		public const string BadCursor = "'afterId' is not a cursor of this list";
	}

	public static class DaemonHealth
	{
		/// <summary>Log files are counted by this pattern in the log folder.</summary>
		public const string LogFilePattern = "*.log*";
	}
}
