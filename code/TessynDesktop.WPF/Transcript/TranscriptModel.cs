using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Rendering;

namespace TessynDesktop.WPF.Transcript;

/// <summary>
/// The loaded part of one session's transcript: a contiguous run of entries (tail first, older pages prepended, new entries
/// appended) with their rendered views. Plain data; the session pane view model drives it.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class TranscriptModel
{
	public TranscriptModel(string nodeId, int epoch, int total, IReadOnlyList<SessionEntryDto> entries, IReadOnlyList<TranscriptEntryView> views, string? fileId = null)
	{
		NodeId = nodeId;
		Epoch = epoch;
		FileId = fileId;
		Total = total;
		Entries = [.. entries];
		Views = [.. views];
	}

	public string NodeId { get; }

	/// <summary>Transcript epoch; a different epoch from the daemon means the history was rewritten and everything must reload.</summary>
	public int Epoch { get; }

	/// <summary>Content generation of the session file the entries came from (the daemon's content store changes it on every rewrite, including a rewrite made by another process); <c>null</c> without a content store.</summary>
	public string? FileId { get; }

	public int Total { get; private set; }

	/// <summary>
	/// Whether <paramref name="page"/>, read from the daemon now, belongs to a different history than the loaded entries: another epoch, another file
	/// generation, or a file that now holds fewer entries than the newest loaded index. Entries are numbered from the start of the file, so after any of
	/// these a read "after the last loaded index" finds nothing and everything appended since would stay invisible until the session is reopened.
	/// </summary>
	public bool HistoryChanged(SessionEntriesResult page) =>
		page.Epoch != Epoch
		|| (FileId != null && page.FileId != null && page.FileId != FileId)
		|| page.Total <= LastIndex;

	public List<SessionEntryDto> Entries { get; }

	public List<TranscriptEntryView> Views { get; }

	/// <summary>Index of the oldest loaded entry; <see cref="Total"/> when none is loaded.</summary>
	public int FirstIndex => Entries.Count == 0 ? Total : Entries[0].Index;

	/// <summary>Index of the newest loaded entry; -1 when none is loaded.</summary>
	public int LastIndex => Entries.Count == 0 ? -1 : Entries[^1].Index;

	/// <summary>Whether older entries exist that are not loaded.</summary>
	public bool HasOlder => FirstIndex > 0;

	/// <summary>Whether newer entries exist that are not loaded (the model came from a jump and stops short of the end).</summary>
	public bool HasNewer => LastIndex + 1 < Total;

	public void Prepend(IReadOnlyList<SessionEntryDto> entries, IReadOnlyList<TranscriptEntryView> views)
	{
		Entries.InsertRange(0, entries);
		Views.InsertRange(0, views);
	}

	public void Append(IReadOnlyList<SessionEntryDto> entries, IReadOnlyList<TranscriptEntryView> views, int total)
	{
		Entries.AddRange(entries);
		Views.AddRange(views);
		Total = total;
	}

	public void UpdateTotal(int total) => Total = total;

	public void Replace(SessionEntryDto entry, TranscriptEntryView view)
	{
		var position = Entries.FindIndex(e => e.Index == entry.Index);
		if (position < 0)
			return;

		Entries[position] = entry;
		Views[position] = view;
	}
}
