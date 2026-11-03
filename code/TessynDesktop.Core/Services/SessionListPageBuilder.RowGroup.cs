using System;
using System.Collections.Generic;
using System.Linq;
using TessynDesktop.Models.Agent;

namespace TessynDesktop.Services;

/// <remarks>Created by Claude</remarks>
public sealed partial class SessionListPageBuilder
{
	/// <summary>A section with its already ordered rows; <see cref="Newest"/> drives the order of Directory groups.</summary>
	private sealed class RowGroup
	{
		public RowGroup(SessionListSectionModel section, List<SessionListRowModel> rows)
		{
			Section = section;
			Rows    = rows;
			Newest  = rows.Where(r => r.LastActivityUtc.HasValue).Select(r => r.LastActivityUtc).DefaultIfEmpty(null).Max();
		}

		public SessionListSectionModel Section { get; }

		public List<SessionListRowModel> Rows { get; }

		public DateTimeOffset? Newest { get; }
	}

	/// <summary>The rows of one page with the sections they reference.</summary>
	private sealed record PageSlice(List<SessionListSectionModel> Sections, List<SessionListRowModel> Rows);
}
