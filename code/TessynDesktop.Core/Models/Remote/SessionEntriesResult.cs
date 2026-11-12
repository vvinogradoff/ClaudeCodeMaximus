using System.Collections.Generic;

namespace TessynDesktop.Models.Remote;

/// <summary>A page of transcript entries (contract section 3.10).</summary>
/// <param name="NodeId">The session's NodeId.</param>
/// <param name="Epoch">Transcript epoch the page belongs to.</param>
/// <param name="Total">Number of parsed entries in the file.</param>
/// <param name="Entries">The page, ascending by index.</param>
/// <remarks>Created by Claude</remarks>
public sealed record SessionEntriesResult(string NodeId, int Epoch, int Total, IReadOnlyList<SessionEntryDto> Entries);
