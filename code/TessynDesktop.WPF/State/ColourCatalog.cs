using System.Text.RegularExpressions;

namespace TessynDesktop.WPF.State;

/// <summary>
/// Every colour the user can change, with the defaults of the Avalonia client: the recency backgrounds of the session rows (<c>SessionNodeViewModel.RefreshRecencyBrush</c>),
/// the status squares of <c>SessionTreeView.axaml</c>, the Focus kinds (<c>AttentionKindPill</c>, Todo and Alert follow the theme there) and the tree selection.
/// </summary>
/// <remarks>Created by Claude</remarks>
public static partial class ColourCatalog
{
	public const string AppGroup = "Application";
	public const string PageGroup = "Transcript page";
	public const string RecentGroup = "Recent and active sessions";
	public const string StatusGroup = "Session status";
	public const string FocusGroup = "Focus kinds";
	public const string TreeGroup = "Tree";

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

	// The main application colours (Fluent brushes) and the transcript page (CSS variables).
	public const string WindowBackground = "WindowBackground";
	public const string PanelBackground = "PanelBackground";
	public const string InputBackground = "InputBackground";
	public const string Text = "Text";
	public const string SecondaryText = "SecondaryText";
	public const string Accent = "Accent";
	public const string AccentText = "AccentText";
	public const string Border = "Border";
	public const string Hover = "Hover";
	public const string TitleBarBackground = "TitleBarBackground";
	public const string TitleBarText = "TitleBarText";
	public const string PageBackground = "PageBackground";
	public const string PageText = "PageText";
	public const string PageUser = "PageUser";
	public const string PageAuto = "PageAuto";
	public const string PageAutoHeader = "PageAutoHeader";
	public const string PageSystemText = "PageSystemText";
	public const string PageHeader = "PageHeader";
	public const string PageCompaction = "PageCompaction";
	public const string PageCompactionBorder = "PageCompactionBorder";
	public const string PageCode = "PageCode";
	public const string PageInlineCode = "PageInlineCode";
	public const string PageLine = "PageLine";
	public const string PageLink = "PageLink";

	/// <summary>The colours in the order of the Colours tab.</summary>
	public static IReadOnlyList<ColourDefinition> All { get; } =
	[
		new(WindowBackground, "Window background", AppGroup, "#F3F3F3", "#202020", ["ApplicationBackgroundBrush", "SolidBackgroundFillColorBaseBrush"]),
		new(PanelBackground, "Panels and cards", AppGroup, "#F9F9F9", "#2B2B2B", ["SolidBackgroundFillColorSecondaryBrush", "SolidBackgroundFillColorTertiaryBrush", "CardBackgroundFillColorDefaultBrush", "LayerFillColorDefaultBrush"]),
		new(InputBackground, "Input and button background", AppGroup, "#FFFFFF", "#323232", ["ControlFillColorDefaultBrush", "ControlFillColorInputActiveBrush", "ControlSolidFillColorDefaultBrush"]),
		new(Text, "Text", AppGroup, "#1A1A1A", "#FFFFFF", ["TextFillColorPrimaryBrush"]),
		new(SecondaryText, "Secondary text", AppGroup, "#616161", "#ADADAD", ["TextFillColorSecondaryBrush"]),
		new(Accent, "Accent (also links and marks on the transcript page)", AppGroup, "#0078D4", "#0078D4", ["AccentFillColorDefaultBrush"], "--accent"),
		new(AccentText, "Text on the accent", AppGroup, "#FFFFFF", "#000000", ["TextOnAccentFillColorPrimaryBrush"]),
		new(Border, "Borders and dividers", AppGroup, "#E0E0E0", "#404040", ["ControlStrokeColorDefaultBrush", "DividerStrokeColorDefaultBrush", "CardStrokeColorDefaultBrush"]),
		new(Hover, "Hover highlight", AppGroup, "#0A000000", "#0FFFFFFF", ["SubtleFillColorSecondaryBrush"]),
		new(TitleBarBackground, "Title bar and status bar", AppGroup, "#F3F3F3", "#2B2B2B", ["TitleBarBackgroundBrush", "StatusBarBackgroundBrush"]),
		new(TitleBarText, "Title bar text", AppGroup, "#1A1A1A", "#FFFFFF", ["TitleBarForegroundBrush", "MeterTextBrush"]),
		new(PageBackground, "Page background", PageGroup, "#F9F9F9", "#202020", null, "--bg"),
		new(PageText, "Page text", PageGroup, "#1A1A1A", "#FFFFFF", null, "--fg"),
		new(PageUser, "Your prompts", PageGroup, "#D6E7F4", "#173952", null, "--user"),
		new(PageAuto, "Automatic prompts", PageGroup, "#C7E3E0", "#224844", null, "--auto"),
		new(PageAutoHeader, "Automatic prompts: header", PageGroup, "#00695C", "#80CBC4", null, "--autoh"),
		new(PageSystemText, "System text and quotes", PageGroup, "#616161", "#ADADAD", null, "--sys"),
		new(PageHeader, "Entry headers and times", PageGroup, "#616161", "#ADADAD", null, "--hdr"),
		new(PageCompaction, "Compaction note: background", PageGroup, "#FFF4CE", "#433519", null, "--comp"),
		new(PageCompactionBorder, "Compaction note: border", PageGroup, "#D6B656", "#8A6D1C", null, "--compb"),
		new(PageCode, "Code blocks", PageGroup, "#F3F3F3", "#1A1A1A", null, "--code"),
		new(PageInlineCode, "Inline code", PageGroup, "#EBEBEB", "#383838", null, "--icode"),
		new(PageLine, "Lines and borders", PageGroup, "#E0E0E0", "#404040", null, "--line"),
		new(PageLink, "Links", PageGroup, "#0060AA", "#73B5E7", null, "--link"),
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
	];

	/// <summary>The definition with this key; null when unknown.</summary>
	public static ColourDefinition? Find(string key) => All.FirstOrDefault(d => d.Key == key);

	/// <summary>The effective colour: the user's, else the default of the theme.</summary>
	public static string Effective(ColourDefinition definition, IReadOnlyDictionary<string, string>? overrides, bool dark) =>
		overrides != null && overrides.TryGetValue(definition.Key, out var value) && IsValid(value) ? Normalize(value) : dark ? definition.DarkDefault : definition.LightDefault;

	/// <summary>The CSS variables the transcript page gets: only the colours the user changed in this theme (the page keeps its own for the rest), <c>--name</c> to hex.</summary>
	public static IReadOnlyDictionary<string, string> PageVariables(ColourSettings settings, bool dark)
	{
		var changed = settings.For(dark);
		var variables = new Dictionary<string, string>(StringComparer.Ordinal);
		foreach (var definition in All)
		{
			if (definition.Css != null && changed.TryGetValue(definition.Key, out var value) && IsValid(value))
				variables[definition.Css] = ToCss(value);
		}

		return variables;
	}

	/// <summary>The colour as CSS writes it: <c>#RRGGBB</c>, or <c>#RRGGBBAA</c> (WPF's <c>#AARRGGBB</c> has the alpha first).</summary>
	public static string ToCss(string hex)
	{
		var normal = Normalize(hex);
		return normal.Length == 9 ? "#" + normal.Substring(3) + normal.Substring(1, 2) : normal;
	}

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
