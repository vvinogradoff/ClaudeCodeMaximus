namespace TessynDesktop.WPF.State;

/// <summary>One standard CSS / HTML colour name with its value as <c>#RRGGBB</c>.</summary>
/// <remarks>Created by Claude</remarks>
public sealed record NamedColour(string Name, string Hex);

/// <summary>
/// The 140 standard CSS / HTML named colours (<c>aliceblue</c> to <c>yellowgreen</c>) with their exact values, for the colour picker's Named colours section (FR.9.19). This is the
/// CSS list, not <c>System.Windows.Media.Colors</c> (which has another set and includes <c>Transparent</c>). The grey colours use the spelling <c>gray</c>; the filter also
/// accepts <c>grey</c>. Contains no WPF types.
/// </summary>
/// <remarks>Created by Claude</remarks>
public static class NamedColours
{
	/// <summary>All the names, in alphabetical order.</summary>
	public static IReadOnlyList<NamedColour> All { get; } =
	[
		new("aliceblue", "#F0F8FF"), new("antiquewhite", "#FAEBD7"), new("aqua", "#00FFFF"), new("aquamarine", "#7FFFD4"), new("azure", "#F0FFFF"),
		new("beige", "#F5F5DC"), new("bisque", "#FFE4C4"), new("black", "#000000"), new("blanchedalmond", "#FFEBCD"), new("blue", "#0000FF"),
		new("blueviolet", "#8A2BE2"), new("brown", "#A52A2A"), new("burlywood", "#DEB887"), new("cadetblue", "#5F9EA0"), new("chartreuse", "#7FFF00"),
		new("chocolate", "#D2691E"), new("coral", "#FF7F50"), new("cornflowerblue", "#6495ED"), new("cornsilk", "#FFF8DC"), new("crimson", "#DC143C"),
		new("cyan", "#00FFFF"), new("darkblue", "#00008B"), new("darkcyan", "#008B8B"), new("darkgoldenrod", "#B8860B"), new("darkgray", "#A9A9A9"),
		new("darkgreen", "#006400"), new("darkkhaki", "#BDB76B"), new("darkmagenta", "#8B008B"), new("darkolivegreen", "#556B2F"), new("darkorange", "#FF8C00"),
		new("darkorchid", "#9932CC"), new("darkred", "#8B0000"), new("darksalmon", "#E9967A"), new("darkseagreen", "#8FBC8F"), new("darkslateblue", "#483D8B"),
		new("darkslategray", "#2F4F4F"), new("darkturquoise", "#00CED1"), new("darkviolet", "#9400D3"), new("deeppink", "#FF1493"), new("deepskyblue", "#00BFFF"),
		new("dimgray", "#696969"), new("dodgerblue", "#1E90FF"), new("firebrick", "#B22222"), new("floralwhite", "#FFFAF0"), new("forestgreen", "#228B22"),
		new("fuchsia", "#FF00FF"), new("gainsboro", "#DCDCDC"), new("ghostwhite", "#F8F8FF"), new("gold", "#FFD700"), new("goldenrod", "#DAA520"),
		new("gray", "#808080"), new("green", "#008000"), new("greenyellow", "#ADFF2F"), new("honeydew", "#F0FFF0"), new("hotpink", "#FF69B4"),
		new("indianred", "#CD5C5C"), new("indigo", "#4B0082"), new("ivory", "#FFFFF0"), new("khaki", "#F0E68C"), new("lavender", "#E6E6FA"),
		new("lavenderblush", "#FFF0F5"), new("lawngreen", "#7CFC00"), new("lemonchiffon", "#FFFACD"), new("lightblue", "#ADD8E6"), new("lightcoral", "#F08080"),
		new("lightcyan", "#E0FFFF"), new("lightgoldenrodyellow", "#FAFAD2"), new("lightgray", "#D3D3D3"), new("lightgreen", "#90EE90"), new("lightpink", "#FFB6C1"),
		new("lightsalmon", "#FFA07A"), new("lightseagreen", "#20B2AA"), new("lightskyblue", "#87CEFA"), new("lightslategray", "#778899"), new("lightsteelblue", "#B0C4DE"),
		new("lightyellow", "#FFFFE0"), new("lime", "#00FF00"), new("limegreen", "#32CD32"), new("linen", "#FAF0E6"), new("magenta", "#FF00FF"),
		new("maroon", "#800000"), new("mediumaquamarine", "#66CDAA"), new("mediumblue", "#0000CD"), new("mediumorchid", "#BA55D3"), new("mediumpurple", "#9370DB"),
		new("mediumseagreen", "#3CB371"), new("mediumslateblue", "#7B68EE"), new("mediumspringgreen", "#00FA9A"), new("mediumturquoise", "#48D1CC"), new("mediumvioletred", "#C71585"),
		new("midnightblue", "#191970"), new("mintcream", "#F5FFFA"), new("mistyrose", "#FFE4E1"), new("moccasin", "#FFE4B5"), new("navajowhite", "#FFDEAD"),
		new("navy", "#000080"), new("oldlace", "#FDF5E6"), new("olive", "#808000"), new("olivedrab", "#6B8E23"), new("orange", "#FFA500"),
		new("orangered", "#FF4500"), new("orchid", "#DA70D6"), new("palegoldenrod", "#EEE8AA"), new("palegreen", "#98FB98"), new("paleturquoise", "#AFEEEE"),
		new("palevioletred", "#DB7093"), new("papayawhip", "#FFEFD5"), new("peachpuff", "#FFDAB9"), new("peru", "#CD853F"), new("pink", "#FFC0CB"),
		new("plum", "#DDA0DD"), new("powderblue", "#B0E0E6"), new("purple", "#800080"), new("red", "#FF0000"), new("rosybrown", "#BC8F8F"),
		new("royalblue", "#4169E1"), new("saddlebrown", "#8B4513"), new("salmon", "#FA8072"), new("sandybrown", "#F4A460"), new("seagreen", "#2E8B57"),
		new("seashell", "#FFF5EE"), new("sienna", "#A0522D"), new("silver", "#C0C0C0"), new("skyblue", "#87CEEB"), new("slateblue", "#6A5ACD"),
		new("slategray", "#708090"), new("snow", "#FFFAFA"), new("springgreen", "#00FF7F"), new("steelblue", "#4682B4"), new("tan", "#D2B48C"),
		new("teal", "#008080"), new("thistle", "#D8BFD8"), new("tomato", "#FF6347"), new("turquoise", "#40E0D0"), new("violet", "#EE82EE"),
		new("wheat", "#F5DEB3"), new("white", "#FFFFFF"), new("whitesmoke", "#F5F5F5"), new("yellow", "#FFFF00"), new("yellowgreen", "#9ACD32"),
	];

	/// <summary>The names that match part of <paramref name="filter"/> (case-insensitive; <c>grey</c> is read as <c>gray</c>); every name for an empty filter.</summary>
	public static IReadOnlyList<NamedColour> Filter(string? filter)
	{
		var text = (filter ?? string.Empty).Trim().ToLowerInvariant().Replace("grey", "gray", StringComparison.Ordinal);
		return text.Length == 0 ? All : All.Where(c => c.Name.Contains(text, StringComparison.Ordinal)).ToList();
	}

	/// <summary>The name(s) of the colour with this red, green and blue (the opacity is ignored), joined by " / " for synonyms such as <c>aqua</c> and <c>cyan</c>; null when it has none.</summary>
	public static string? NameOf(string hex)
	{
		if (!ColourCatalog.IsValid(hex))
			return null;

		var normal = ColourCatalog.Normalize(hex);
		var rgb = "#" + normal.Substring(normal.Length - 6);
		var names = All.Where(c => string.Equals(c.Hex, rgb, StringComparison.Ordinal)).Select(c => c.Name).ToList();
		return names.Count == 0 ? null : string.Join(" / ", names);
	}
}
