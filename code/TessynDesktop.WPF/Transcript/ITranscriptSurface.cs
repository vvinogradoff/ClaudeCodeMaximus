using TessynDesktop.WPF.Rendering;

namespace TessynDesktop.WPF.Transcript;

/// <summary>
/// The transcript view as the session pane sees it: a single shared surface (one WebView2) that shows one session at a time.
/// Implemented over WebView2 in the app and by a recording fake in tests. All members are called on the UI thread.
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface ITranscriptSurface
{
	/// <summary>Replaces the content with <paramref name="entries"/> (ascending); scrolls to the newest entry when <paramref name="toBottom"/>.</summary>
	/// <param name="hasNewer">Newer entries exist that are not loaded (a view that started at a jump target): the page asks for them when scrolled to the end.</param>
	void Load(string key, IReadOnlyList<TranscriptEntryView> entries, bool hasOlder, bool toBottom, bool hasNewer = false);

	/// <summary>Adds an older page above the content, keeping the scroll position. An empty page with <paramref name="hasOlder"/> false ends paging.</summary>
	void Prepend(IReadOnlyList<TranscriptEntryView> entries, bool hasOlder);

	/// <summary>Adds newer entries below; the page follows them when it is scrolled to the bottom.</summary>
	/// <param name="hasNewer">More newer entries remain unloaded after these (see <c>Load</c>).</param>
	void Append(IReadOnlyList<TranscriptEntryView> entries, bool hasNewer = false);

	/// <summary>Replaces one entry (e.g. its full content after a truncated one was expanded).</summary>
	void Replace(TranscriptEntryView entry);

	/// <summary>Highlights <paramref name="term"/> in the rendered entries and jumps to a match; <paramref name="matchingIndexes"/> are the entries containing it.</summary>
	void Find(string term, IReadOnlyList<int> matchingIndexes);

	/// <summary>Moves to the next (+1) or previous (-1) match.</summary>
	void FindStep(int direction);

	/// <summary>Removes the highlights.</summary>
	void ClearFind();

	/// <summary>
	/// Scrolls entry <paramref name="index"/> into view (it must be loaded) and highlights <paramref name="quote"/> in it, or the whole
	/// entry when the quote is null or cannot be located in the rendered text. The highlight stays until the next load. With
	/// <paramref name="highlight"/> false the entry is only scrolled into view (Back restores a position).
	/// </summary>
	void GoToEntry(int index, string? quote, bool highlight);

	/// <summary>The user scrolled; the page reports the entry at the top of the view (argument: the position).</summary>
	event EventHandler<ViewAnchorEventArgs>? ViewChanged;

	/// <summary>The user clicked a local-path chip under an assistant entry (FR.26.5).</summary>
	event EventHandler<PathClickedEventArgs>? PathClicked;

	/// <summary>The first paint after a <see cref="Load"/> happened.</summary>
	event EventHandler<TranscriptPaintedEventArgs>? Painted;

	/// <summary>The user scrolled near the top and older entries exist.</summary>
	event EventHandler? OlderRequested;

	/// <summary>The user scrolled to the end of loaded entries and newer ones exist.</summary>
	event EventHandler? NewerRequested;

	/// <summary>The user asked for the full content of a truncated entry (argument: entry index).</summary>
	event EventHandler<int>? ExpandRequested;

	/// <summary>The user clicked a link (argument: the raw href; validated by the receiver).</summary>
	event EventHandler<string>? LinkClicked;

	/// <summary>Ctrl+F was pressed inside the page.</summary>
	event EventHandler? FindRequested;

	/// <summary>F3 / Shift+F3 was pressed inside the page (argument: direction).</summary>
	event EventHandler<int>? FindStepRequested;

	/// <summary>The page restarted (reload or render-process recovery) and is empty: the owner must replay the current <see cref="Load"/>.</summary>
	event EventHandler? PageRestarted;

	/// <summary>The page reports the current match position.</summary>
	event EventHandler<FindStatusEventArgs>? FindStatusChanged;
}
