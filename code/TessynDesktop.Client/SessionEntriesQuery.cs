namespace TessynDesktop.Client;

/// <summary>
/// The bounds of a <c>session.entries</c> call (P1.F). At most one of <see cref="BeforeIndex"/>, <see cref="AfterIndex"/>,
/// <see cref="AfterSeq"/> and <see cref="BeforeEntryId"/> may be set; with none the page is the newest one.
/// </summary>
/// <param name="BeforeIndex">Entries before this dense index.</param>
/// <param name="AfterIndex">Entries after this dense index.</param>
/// <param name="AfterSeq">Entries the client does not hold yet: those from ordinal <c>AfterSeq</c> on (the <c>seq</c> of its last page or <c>entries.appended</c> event).</param>
/// <param name="BeforeEntryId">Entries before the entry with this stable id.</param>
/// <param name="FileId">Only with <see cref="AfterSeq"/>: fail with "Session history changed" when the file is no longer this content generation.</param>
/// <param name="Limit">Most entries to return.</param>
/// <param name="AroundEntryId">The page centred on the entry with this stable id (a Focus reference's <c>entryId</c>); the entry is on the page unless it no longer exists.</param>
/// <remarks>Created by Claude</remarks>
public sealed record SessionEntriesQuery(
	int? BeforeIndex = null,
	int? AfterIndex = null,
	long? AfterSeq = null,
	string? BeforeEntryId = null,
	string? FileId = null,
	int? Limit = null,
	string? AroundEntryId = null);
