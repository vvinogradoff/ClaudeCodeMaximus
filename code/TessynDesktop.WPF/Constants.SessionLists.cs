namespace TessynDesktop.WPF;

/// <summary>Names used by the Recent and Tags tabs, the Recent dropdown and the favourite toggles (W3).</summary>
/// <remarks>Created by Claude</remarks>
public static partial class Constants
{
	/// <summary>Tab headers, command ids and accessible names of the session lists.</summary>
	public static class SessionLists
	{
		public const string RecentTabTitle = "Recent";
		public const string TagsTabTitle = "Tags";
		public const string ToggleFavoriteCommandId = "session.toggleFavorite";
		public const string ToggleFavoriteTitle = "Toggle favourite";
		public const string FavoriteMenuGroup = "session";
		public const int FavoriteMenuOrder = 90;
		public const int RecentTabOrder = 10;
		public const int TagsTabOrder = 20;
		public const int RecentDropdownOrder = 10;
		public const int FavoriteButtonOrder = 10;

		/// <summary>Automation names the E2E tests find the controls by.</summary>
		public const string RecentListName = "Recent sessions";
		public const string TagSearchBoxName = "Tag search";
		public const string FavoriteButtonName = "Favourite";
		public const string HideButtonPrefix = "Hide ";
		/// <summary>The Tag of the open button of a row; the dropdown closes when it is clicked.</summary>
		public const string OpenTag = "open";
		public const string OpenButtonPrefix = "Open ";
		public const string TagChipPrefix = "Tag ";
		public const string RecentDropdownName = "Recent dropdown";

		public const string RecentEmptyText = "No sessions in the last 24 hours.";
		public const string TagsSelectTagsText = "Select one or more tags to list their sessions.";
		public const string TagsNoSessionsText = "No sessions carry the selected tags.";
		public const string TagsNoTagsText = "No session has any tag yet.";
		public const string TagsNoMatchText = "No tag matches.";
		public const string DaemonErrorFormat = "{0}";
	}
}
