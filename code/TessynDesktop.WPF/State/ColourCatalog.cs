using System.Text.RegularExpressions;

namespace TessynDesktop.WPF.State;

/// <summary>
/// Every colour the user can change, with the defaults of the Avalonia client: the recency backgrounds of the session rows (<c>SessionNodeViewModel.RefreshRecencyBrush</c>),
/// the status squares of <c>SessionTreeView.axaml</c>, the Focus kinds (<c>AttentionKindPill</c>, Todo and Alert follow the theme there) and the tree selection.
/// </summary>
/// <remarks>Created by Claude</remarks>
public static partial class ColourCatalog
{
	public const string RecentGroup = "Recent and active sessions";
	public const string StatusGroup = "Session status";
	public const string FocusGroup = "Focus kinds";
	public const string TreeGroup = "Tree";
	public const string TranscriptGroup = "Session output";

	public const string RecentTier1 = "RecentTier1";
	public const string RecentTier2 = "RecentTier2";
	public const string RecentTier3 = "RecentTier3";
	public const string RecentTier4 = "RecentTier4";
	public const string ActiveBorder = "ActiveBorder";
	public const string Running = "StatusRunning";
	public const string Resumable = "StatusResumable";
	public const string FocusCritical = "FocusCritical";
	public const string FocusCriticalText = "FocusCriticalText";
	public const string FocusAsk = "FocusAsk";
	public const string FocusAskText = "FocusAskText";
	public const string FocusTodo = "FocusTodo";
	public const string FocusTodoText = "FocusTodoText";
	public const string FocusAlert = "FocusAlert";
	public const string FocusAlertText = "FocusAlertText";
	public const string FocusDecided = "FocusDecided";
	public const string FocusDecidedText = "FocusDecidedText";
	public const string Selection = "TreeSelection";
	public const string SystemRow = "SystemRow";

	/// <summary>The colours in the order of the Colours tab.</summary>
	public static IReadOnlyList<ColourDefinition> All { get; } =
	[
		new(RecentTier1, "Last 15 minutes", RecentGroup, "#008000", "#008000"),
		new(RecentTier2, "Last hour", RecentGroup, "#90EE90", "#90EE90"),
		new(RecentTier3, "Last 4 hours", RecentGroup, "#F0FFF0", "#F0FFF0"),
		new(RecentTier4, "Last 12 hours (and an unsent draft)", RecentGroup, "#F5FFFA", "#F5FFFA"),
		new(ActiveBorder, "Dotted border: active in the last 24 hours", RecentGroup, "#006400", "#006400"),
		new(Running, "Running (upper square)", StatusGroup, "#8B0000", "#8B0000"),
		new(Resumable, "Resumable in Claude Code (lower square)", StatusGroup, "#1E90FF", "#1E90FF"),
		new(FocusCritical, "Critical: background", FocusGroup, "#8B0000", "#8B0000"),
		new(FocusCriticalText, "Critical: text", FocusGroup, "#FFFFFF", "#FFFFFF"),
		new(FocusAsk, "Question: background", FocusGroup, "#DAA520", "#DAA520"),
		new(FocusAskText, "Question: text", FocusGroup, "#000000", "#000000"),
		new(FocusTodo, "Todo: background", FocusGroup, "#000000", "#FFFFFF"),
		new(FocusTodoText, "Todo: text", FocusGroup, "#FFFFFF", "#000000"),
		new(FocusAlert, "Alert: background", FocusGroup, "#00FFFFFF", "#00000000"),
		new(FocusAlertText, "Alert: text", FocusGroup, "#000000", "#FFFFFF"),
		new(FocusDecided, "Decided: background", FocusGroup, "#707070", "#707070"),
		new(FocusDecidedText, "Decided: text", FocusGroup, "#FFFFFF", "#FFFFFF"),
		new(Selection, "Selected row", TreeGroup, "#CDE6F7", "#264F78"),
		new(SystemRow, "System rows (tool calls)", TranscriptGroup, "#BFE0F7", "#1A3A50"),
	];

	/// <summary>The definition with this key; null when unknown.</summary>
	public static ColourDefinition? Find(string key) => All.FirstOrDefault(d => d.Key == key);

	/// <summary>The effective colour: the user's, else the default of the theme.</summary>
	public static string Effective(ColourDefinition definition, IReadOnlyDictionary<string, string>? overrides, bool dark) =>
		overrides != null && overrides.TryGetValue(definition.Key, out var value) && IsValid(value) ? Normalize(value) : dark ? definition.DarkDefault : definition.LightDefault;

	/// <summary><c>#RGB</c>, <c>#RRGGBB</c> or <c>#AARRGGBB</c>.</summary>
	public static bool IsValid(string? text) => text != null && HexPattern().IsMatch(text.Trim());

	/// <summary>Upper-case <c>#RRGGBB</c> or <c>#AARRGGBB</c>; <c>#RGB</c> is expanded.</summary>
	public static string Normalize(string text)
	{
		var hex = text.Trim().ToUpperInvariant();
		return hex.Length == 4 ? "#" + hex[1] + hex[1] + hex[2] + hex[2] + hex[3] + hex[3] : hex;
	}

	[GeneratedRegex("^#([0-9A-Fa-f]{3}|[0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})$")]
	private static partial Regex HexPattern();
}
