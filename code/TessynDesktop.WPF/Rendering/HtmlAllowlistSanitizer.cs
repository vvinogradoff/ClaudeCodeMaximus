using System.Text;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using TessynDesktop.WPF.Services;

namespace TessynDesktop.WPF.Rendering;

/// <summary>
/// Allowlist sanitizer for the HTML Markdig produces (review findings V1 and W1). The HTML is parsed with a real HTML parser (AngleSharp)
/// and written out again by this class alone: only the tags and attributes below survive, every text node and attribute value is escaped,
/// and the output is always well-formed. No element that can load a resource (img, video, audio, source, picture, svg, math, object,
/// embed, link, meta, track, ...) and no attribute that can (src, srcset, style, background, poster, ping, ...) can come out, whatever the
/// markdown or the parser did. An <c>href</c> must pass <see cref="UrlPolicy"/> (http/https) or is dropped; table alignment, which Markdig
/// writes as an inline style, becomes a class.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class HtmlAllowlistSanitizer
{
	private static readonly HashSet<string> AllowedTags = new(StringComparer.Ordinal)
	{
		"p", "br", "hr", "h1", "h2", "h3", "h4", "h5", "h6", "ul", "ol", "li", "blockquote", "pre", "code", "em", "strong", "del", "ins", "mark", "sub", "sup",
		"a", "table", "thead", "tbody", "tfoot", "tr", "th", "td", "span", "div",
	};

	/// <summary>Elements removed together with everything inside them (their content is not text to show).</summary>
	private static readonly HashSet<string> DroppedWithContent = new(StringComparer.Ordinal)
	{
		"script", "style", "svg", "math", "object", "embed", "iframe", "frame", "frameset", "template", "noscript", "audio", "video", "picture",
		"source", "track", "img", "link", "meta", "base", "form", "input", "button", "select", "textarea", "head", "title", "canvas", "applet", "portal",
	};

	private static readonly HashSet<string> VoidTags = new(StringComparer.Ordinal) { "br", "hr" };

	private readonly UrlPolicy _urlPolicy;
	private readonly HtmlParser _parser = new();

	public HtmlAllowlistSanitizer(UrlPolicy urlPolicy)
	{
		_urlPolicy = urlPolicy;
	}

	/// <summary>The allowlisted, well-formed rendition of <paramref name="html"/>.</summary>
	public string Sanitize(string html)
	{
		if (string.IsNullOrEmpty(html))
			return string.Empty;

		var document = _parser.ParseDocument("<!DOCTYPE html><html><body>" + html + "</body></html>");
		var output = new StringBuilder(html.Length);
		foreach (var node in document.Body!.ChildNodes)
			Write(node, output);

		return output.ToString();
	}

	private void Write(INode node, StringBuilder output)
	{
		switch (node)
		{
			case IText text:
				AppendEscaped(output, text.Data);
				break;
			case IElement element:
				WriteElement(element, output);
				break;
		}
	}

	private void WriteElement(IElement element, StringBuilder output)
	{
		var name = element.LocalName.ToLowerInvariant();
		if (DroppedWithContent.Contains(name))
			return;

		if (!AllowedTags.Contains(name))
		{
			foreach (var child in element.ChildNodes)
				Write(child, output);
			return;
		}

		output.Append('<').Append(name);
		AppendAttributes(element, name, output);
		output.Append('>');
		if (VoidTags.Contains(name))
			return;

		foreach (var child in element.ChildNodes)
			Write(child, output);
		output.Append("</").Append(name).Append('>');
	}

	private void AppendAttributes(IElement element, string name, StringBuilder output)
	{
		var classes = new List<string>();
		foreach (var attribute in element.Attributes)
		{
			var key = attribute.Name.ToLowerInvariant();
			var value = attribute.Value;
			switch (key)
			{
				case "class" when name is "a" or "code" or "th" or "td" or "pre" or "span" or "div":
					classes.AddRange(ClassToken().Matches(value).Select(m => m.Value));
					break;
				case "href" when name == "a":
					AppendAttribute(output, "href", _urlPolicy.IsAllowed(value) ? value.Trim() : "#");
					break;
				case "start" when name == "ol" && int.TryParse(value, out var start) && start is >= 0 and <= 100000:
					AppendAttribute(output, "start", start.ToString());
					break;
				case "style" when name is "th" or "td":
					var align = Alignment().Match(value);
					if (align.Success)
						classes.Add("al-" + align.Groups[1].Value.ToLowerInvariant());
					break;
			}
		}

		if (classes.Count > 0)
			AppendAttribute(output, "class", string.Join(' ', classes.Distinct()));
	}

	private static void AppendAttribute(StringBuilder output, string name, string value)
	{
		output.Append(' ').Append(name).Append("=\"");
		AppendEscaped(output, value);
		output.Append('"');
	}

	private static void AppendEscaped(StringBuilder output, string text)
	{
		foreach (var c in text)
		{
			switch (c)
			{
				case '&': output.Append("&amp;"); break;
				case '<': output.Append("&lt;"); break;
				case '>': output.Append("&gt;"); break;
				case '"': output.Append("&quot;"); break;
				case '\'': output.Append("&#39;"); break;
				default: output.Append(c); break;
			}
		}
	}

	[GeneratedRegex(@"[A-Za-z0-9_\-]{1,40}")]
	private static partial Regex ClassToken();

	[GeneratedRegex(@"^\s*text-align\s*:\s*(left|right|center)\s*;?\s*$", RegexOptions.IgnoreCase)]
	private static partial Regex Alignment();
}
