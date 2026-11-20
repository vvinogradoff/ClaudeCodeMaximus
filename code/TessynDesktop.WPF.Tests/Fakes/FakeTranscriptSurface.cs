using TessynDesktop.WPF.Rendering;
using TessynDesktop.WPF.Transcript;

namespace TessynDesktop.WPF.Tests.Fakes;

/// <summary>Records what the session pane asks the transcript surface to do and lets tests raise page events.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class FakeTranscriptSurface : ITranscriptSurface
{
	public List<string> Calls { get; } = [];

	/// <summary>The indexes currently on the page, as the real page would hold them (Load replaces, Prepend/Append extend).</summary>
	public List<int> PageIndexes { get; } = [];

	public string? LoadedKey { get; private set; }

	public bool HasOlder { get; private set; }

	public bool LastLoadToBottom { get; private set; }

	public event EventHandler<TranscriptPaintedEventArgs>? Painted;

	public event EventHandler? OlderRequested;

	public event EventHandler<int>? ExpandRequested;

	public event EventHandler<string>? LinkClicked;

	public event EventHandler<string>? SystemLinkClicked;

	public event EventHandler? FindRequested;

	public event EventHandler<int>? FindStepRequested;

	public event EventHandler<FindStatusEventArgs>? FindStatusChanged;

	public event EventHandler? PageRestarted;

	public void RaisePageRestarted() => PageRestarted?.Invoke(this, EventArgs.Empty);

	public bool HasNewer { get; private set; }

	public event EventHandler? NewerRequested;

	public void NewerLoadFailed() => Calls.Add("newerFailed");

	public void RaiseNewerRequested() => NewerRequested?.Invoke(this, EventArgs.Empty);

	public void Load(string key, IReadOnlyList<TranscriptEntryView> entries, bool hasOlder, bool toBottom, bool hasNewer = false)
	{
		HasNewer = hasNewer;
		Calls.Add($"load:{key}:{entries.Count}");
		LoadedKey = key;
		HasOlder = hasOlder;
		LastLoadToBottom = toBottom;
		PageIndexes.Clear();
		PageIndexes.AddRange(entries.Select(e => e.Index));
	}

	public void Prepend(IReadOnlyList<TranscriptEntryView> entries, bool hasOlder)
	{
		Calls.Add($"prepend:{entries.Count}");
		HasOlder = hasOlder;
		PageIndexes.InsertRange(0, entries.Select(e => e.Index));
	}

	public void Append(IReadOnlyList<TranscriptEntryView> entries, bool hasNewer = false)
	{
		HasNewer = hasNewer;
		Calls.Add($"append:{entries.Count}");
		PageIndexes.AddRange(entries.Select(e => e.Index));
	}

	/// <summary>Every live row the pane sent, in order (null = removed).</summary>
	public List<TranscriptLiveView?> LiveViews { get; } = [];

	public TranscriptLiveView? LastLive => LiveViews.Count == 0 ? null : LiveViews[^1];

	public void SetLive(TranscriptLiveView? live)
	{
		LiveViews.Add(live);
		Calls.Add(live == null ? "live:none" : $"live:{live.Status}");
	}

	public void Replace(TranscriptEntryView entry) => Calls.Add($"replace:{entry.Index}:{entry.Html}");

	public void Find(string term, IReadOnlyList<int> matchingIndexes) => Calls.Add($"find:{term}:{string.Join(",", matchingIndexes)}");

	public void FindStep(int direction) => Calls.Add($"findStep:{direction}");

	public void ClearFind() => Calls.Add("clearFind");

	public event EventHandler<ViewAnchorEventArgs>? ViewChanged;

	public event EventHandler<PathClickedEventArgs>? PathClicked;

	public event EventHandler<PathClickedEventArgs>? PathImageRequested;

	public List<(int Entry, string Path, string? Src, bool Denied)> PathImages { get; } = [];

	public void ShowPathImage(int entryIndex, string path, string? src, bool denied) => PathImages.Add((entryIndex, path, src, denied));

	public void RaisePathImageRequested(int entryIndex, string path) => PathImageRequested?.Invoke(this, new PathClickedEventArgs(entryIndex, path));

	public void GoToEntry(int index, string? quote, bool highlight) => Calls.Add($"goto:{index}:{quote ?? "-"}:{(highlight ? "hl" : "nohl")}");

	public void RaiseViewChanged(int topIndex, bool atBottom) => ViewChanged?.Invoke(this, new ViewAnchorEventArgs(topIndex, atBottom));

	public void RaisePathClicked(int entryIndex, string path) => PathClicked?.Invoke(this, new PathClickedEventArgs(entryIndex, path));

	public void RaisePainted(string key, double jsMs = 1) => Painted?.Invoke(this, new TranscriptPaintedEventArgs(key, jsMs));

	public void RaiseOlderRequested() => OlderRequested?.Invoke(this, EventArgs.Empty);

	public void RaiseExpandRequested(int index) => ExpandRequested?.Invoke(this, index);

	public void RaiseLinkClicked(string url) => LinkClicked?.Invoke(this, url);

	public void RaiseSystemLinkClicked(string url) => SystemLinkClicked?.Invoke(this, url);

	public void RaiseFindRequested() => FindRequested?.Invoke(this, EventArgs.Empty);

	public void RaiseFindStatus(int current, int count) => FindStatusChanged?.Invoke(this, new FindStatusEventArgs(current, count));

	public void RaiseFindStep(int direction) => FindStepRequested?.Invoke(this, direction);
}
