namespace TessynDesktop.Models;

/// <summary>Which entries a page holds and how much of them. With neither bound set the page is the tail of the file.</summary>
/// <param name="BeforeOrdinal">Entries just before this ordinal (clamped to the file).</param>
/// <param name="AfterOrdinal">Entries just after this ordinal (clamped to the file); wins over <paramref name="BeforeOrdinal"/>.</param>
/// <param name="MaxEntries">Most entries on the page.</param>
/// <param name="MaxBytes">Byte budget of the page: entries are added while the total stays within it, but a page always has one.</param>
/// <param name="EntryMaxBytes">Cap of one entry (header line included): longer entries are cut and fetched on by range.</param>
/// <remarks>Created by Claude</remarks>
public sealed record SessionContentPageRequest(
	int? BeforeOrdinal,
	int? AfterOrdinal,
	int MaxEntries,
	int MaxBytes = Constants.ContentStore.DefaultPageMaxBytes,
	int EntryMaxBytes = Constants.ContentStore.DefaultEntryMaxBytes);
