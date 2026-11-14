using System.Collections.Generic;

namespace TessynDesktop.Models.Remote;

/// <summary>
/// Params of <c>entries.appended</c> (P1.F): entries <c>fromSeq</c> up to but excluding <c>toSeq</c> were appended to the session's
/// file generation <c>fileId</c>. The ids of the new entries are <c>{fileId}:{ordinal}</c> for the ordinals of that range.
/// </summary>
/// <param name="NodeId">The session's NodeId.</param>
/// <param name="FileId">Content generation the range belongs to; a different id than the client holds means "reload" (see <c>session.reset</c>).</param>
/// <param name="FromSeq">First new ordinal.</param>
/// <param name="ToSeq">One past the last new ordinal; the session's <c>seq</c> after the append.</param>
/// <param name="Entries">The new entries, ascending, inline when they fit the byte cap; omitted (ids only) otherwise: fetch them with <c>session.entries</c> and <c>afterSeq = fromSeq - 1</c>.</param>
/// <remarks>Created by Claude</remarks>
public sealed record EntriesAppendedPayload(string NodeId, string FileId, long FromSeq, long ToSeq, IReadOnlyList<SessionEntryDto>? Entries = null);
