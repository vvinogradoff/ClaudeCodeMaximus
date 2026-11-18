using TessynDesktop.WPF.Rendering;

namespace TessynDesktop.WPF.Transcript;

/// <summary>
/// The original Markdown of the entries the page shows, by entry index (FR.2.13). The page receives the rendered HTML only, so a selection that
/// covers whole entries is answered from here instead of from a conversion of the HTML. A truncated entry is not kept (its text is cut), and a
/// load drops everything.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class TranscriptCopySource
{
	private const string PartSeparator = "\n\n";

	private readonly Dictionary<int, string> _sources = new();

	public void Reset() => _sources.Clear();

	public void Remember(IEnumerable<TranscriptEntryView> entries)
	{
		foreach (var entry in entries)
		{
			if (entry.Truncated || entry.SourceMarkdown == null)
				_sources.Remove(entry.Index);
			else
				_sources[entry.Index] = entry.SourceMarkdown;
		}
	}

	/// <summary>The text for the clipboard: a whole entry as its original Markdown, anything else as the page converted it, entries separated by a blank line.</summary>
	/// <param name="useSources">False when the parts come from an older load whose entry indexes may mean other entries now.</param>
	public string Compose(IEnumerable<CopyPart> parts, bool useSources)
	{
		var texts = parts
			.Select(p => p.Whole && useSources && p.Entry is { } index && _sources.TryGetValue(index, out var source) ? source : p.Markdown)
			.Select(t => t.Trim())
			.Where(t => t.Length > 0);
		return string.Join(PartSeparator, texts);
	}
}
