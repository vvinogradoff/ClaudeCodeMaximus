namespace TessynDesktop.WPF;

/// <summary>Names and texts of the import picker and the global search (W7, FR.13).</summary>
/// <remarks>Created by Claude</remarks>
public static partial class Constants
{
	public static class Import
	{
		public const string OpenCommandId = "app.importSessions";
		public const string OpenCommandTitle = "Import sessions…";
		public const string OpenCommandGesture = "Ctrl+I";
		public const string TreeCommandId = "tree.importSessions";
		public const string TreeCommandTitle = "Import sessions here…";
		public const string TreeMenuGroup = "add";
		public const int TreeMenuOrder = 12;
		public const int OpenButtonOrder = 30;
		public const string OpenButtonText = "Import";

		public const string WindowTitle = "Import sessions";
		public const string NotConnected = "The daemon is not connected yet, so sessions cannot be imported.";
		public const string NoDirectory = "Add a directory to the tree first: sessions are imported into a directory or a group.";

		/// <summary>The most sessions one page of the list holds; "Load more" asks for the next page.</summary>
		public const int PageSize = 100;

		/// <summary>The most ids <c>import.titles</c> takes at once.</summary>
		public const int TitleBatch = 200;

		public static readonly TimeSpan TitlePollInterval = TimeSpan.FromMilliseconds(1500);
		public const int MaxTitlePolls = 40;

		public const string OpenButtonAutomationName = "Import sessions";
		public const string SourceName = "Import source";
		public const string TargetName = "Import target";
		public const string GlobalName = "Global search";
		public const string SearchBoxName = "Import search";
		public const string SearchButtonName = "Search sessions";
		public const string ListName = "Import list";
		public const string ImportButtonName = "Import selected";
		public const string CloseButtonName = "Close import";
		public const string LoadMoreName = "Load more";
		public const string SelectPrefix = "Select ";
		public const string StatusName = "Import status";

		public const string Loading = "Loading sessions…";
		public const string SearchHint = "Type a search and press Enter to look in every project.";
		public const string NothingFound = "No Claude Code sessions found.";
		public const string FoundFormat = "{0} sessions ({1} shown).";
		public const string ListFailed = "The sessions could not be listed: ";
		public const string SearchFailed = "The search failed: ";
		public const string ImportFailed = "The import failed: ";
		public const string ImportedFormat = "Imported {0}.";
		public const string FailedFormat = "{0} could not be imported: {1}";
		public const string TitlesLeftFormat = "Generating titles… {0} left";
		public const string AlreadyImported = "Already imported";
		public const string EmptySession = "Empty";
		public const string Untitled = "(untitled session)";
		public const string OtherProject = "Project {0}";
	}
}
