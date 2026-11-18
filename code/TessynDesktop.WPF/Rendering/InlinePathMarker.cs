using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace TessynDesktop.WPF.Rendering;

/// <summary>
/// Marks the local file paths of an assistant entry inside its rendered HTML (FR.26.5): each occurrence becomes
/// <c>&lt;span class="pth" data-j="N"&gt;path&lt;/span&gt;</c> where N is the index into the entry's path list, so the path stays exactly where it is in the
/// text. The first occurrence of an image or video path is followed by an empty <c>&lt;span class="pslot" data-j="N"&gt;</c> the page fills with the image preview or the
/// first frame of the video (directly under the path). Runs on the already sanitized HTML and writes only its own fixed markup and the escaped text, so it adds no
/// attribute or element that can load a resource. Paths inside links are left alone.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class InlinePathMarker
{
	/// <summary>Image types the page may preview inline (svg is a text type here and is never rendered, so it gets the path only).</summary>
	private static readonly HashSet<string> InlineImageExtensions = new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp" };

	/// <summary>True when the path names a video the page previews inline with its first frame (FR.26.6).</summary>
	public static bool IsInlineVideo(string path)
	{
		var dot = path.LastIndexOf('.');
		return dot > 0 && Constants.FileTypes.InlineVideo.Contains(path[dot..]);
	}

	/// <summary>True when the path names an image the page previews inline.</summary>
	public static bool IsInlineImage(string path)
	{
		var dot = path.LastIndexOf('.');
		return dot > 0 && InlineImageExtensions.Contains(path[dot..]);
	}

	/// <summary>The HTML with every occurrence of a path of <paramref name="paths"/> marked; unchanged when none occurs.</summary>
	public string Mark(string html, IReadOnlyList<string> paths)
	{
		if (paths.Count == 0 || string.IsNullOrEmpty(html))
			return html;

		var candidates = new List<(string Text, int Index)>();
		for (var j = 0; j < paths.Count; j++)
		{
			candidates.Add((paths[j], j));
			var unescaped = UnescapeMarkdown(paths[j]);
			if (unescaped != paths[j])
				candidates.Add((unescaped, j));
		}

		candidates.Sort((a, b) => b.Text.Length.CompareTo(a.Text.Length));   // the longest first: a path that is the start of another one must not win
		var document = new HtmlParser().ParseDocument("<!DOCTYPE html><html><body>" + html + "</body></html>");
		var slotted = new HashSet<int>();
		var changed = false;
		foreach (var text in TextNodes(document.Body!).ToList())
			changed |= MarkText(text, candidates, slotted);

		return changed ? document.Body!.InnerHtml : html;
	}

	private static IEnumerable<IText> TextNodes(INode node)
	{
		foreach (var child in node.ChildNodes)
		{
			if (child is IText text)
				yield return text;
			else if (child is IElement element && !element.LocalName.Equals("a", StringComparison.OrdinalIgnoreCase))
			{
				foreach (var nested in TextNodes(element))
					yield return nested;
			}
		}
	}

	private static bool MarkText(IText node, List<(string Text, int Index)> candidates, HashSet<int> slotted)
	{
		var data = node.Data;
		var pieces = new List<INode>();
		var document = node.Owner!;
		var pos = 0;
		var from = 0;
		while (from < data.Length)
		{
			var (at, hit) = FindNext(data, from, candidates);
			if (at < 0)
				break;

			if (at > pos)
				pieces.Add(document.CreateTextNode(data[pos..at]));

			var span = document.CreateElement("span");
			span.ClassName = "pth";
			span.SetAttribute("data-j", hit.Index.ToString());
			span.TextContent = hit.Text;
			pieces.Add(span);
			if ((IsInlineImage(hit.Text) || IsInlineVideo(hit.Text)) && slotted.Add(hit.Index))
			{
				var slot = document.CreateElement("span");
				slot.ClassName = "pslot";
				slot.SetAttribute("data-j", hit.Index.ToString());
				pieces.Add(slot);
			}

			pos = from = at + hit.Text.Length;
		}

		if (pieces.Count == 0)
			return false;

		if (pos < data.Length)
			pieces.Add(document.CreateTextNode(data[pos..]));

		var parent = node.Parent!;
		foreach (var piece in pieces)
			parent.InsertBefore(piece, node);

		parent.RemoveChild(node);
		return true;
	}

	/// <summary>The earliest occurrence (longest candidate first at the same place) that does not start or end inside a longer word or path.</summary>
	private static (int At, (string Text, int Index) Hit) FindNext(string data, int from, List<(string Text, int Index)> candidates)
	{
		var best = -1;
		(string Text, int Index) hit = default;
		foreach (var candidate in candidates)
		{
			var search = from;
			while (search <= data.Length - candidate.Text.Length)
			{
				var at = data.IndexOf(candidate.Text, search, StringComparison.Ordinal);
				if (at < 0 || (best >= 0 && at >= best))
					break;

				if (IsBoundary(data, at, candidate.Text.Length))
				{
					best = at;
					hit = candidate;
					break;
				}

				search = at + 1;
			}
		}

		return (best, hit);
	}

	private static bool IsBoundary(string data, int at, int length)
	{
		if (at > 0 && IsPathChar(data[at - 1]))
			return false;

		var end = at + length;
		return end >= data.Length || !IsPathChar(data[end]);
	}

	private static bool IsPathChar(char c) => char.IsLetterOrDigit(c) || c is '_' or '\\' or '/';

	/// <summary>CommonMark backslash escapes: a backslash before ASCII punctuation is dropped (so <c>\\server\share</c> renders as <c>\server\share</c>).</summary>
	private static string UnescapeMarkdown(string text)
	{
		var result = new System.Text.StringBuilder(text.Length);
		for (var i = 0; i < text.Length; i++)
		{
			if (text[i] == '\\' && i + 1 < text.Length && !char.IsAsciiLetterOrDigit(text[i + 1]) && text[i + 1] is > ' ' and < (char)127)
				i++;

			result.Append(text[i]);
		}

		return result.ToString();
	}
}
