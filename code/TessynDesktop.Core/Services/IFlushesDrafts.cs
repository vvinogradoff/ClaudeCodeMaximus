using System;
using System.Collections.Generic;
using Serilog;

namespace TessynDesktop.Services;

/// <summary>Something that holds unsent text in a debounce and can write it at once (the composer draft, the Focus reply drafts).</summary>
/// <remarks>Created by Claude</remarks>
public interface IFlushesDrafts
{
	/// <summary>Writes the pending draft text now. Cheap and safe to call when nothing is pending.</summary>
	void FlushDrafts();
}

/// <summary>Flushes many <see cref="IFlushesDrafts"/> in one go, for the exit path (FR.23.19): one that fails never stops the rest.</summary>
/// <remarks>Created by Claude</remarks>
public static class DraftFlusher
{
	private static readonly ILogger _log = Log.ForContext(typeof(DraftFlusher));

	/// <summary>Flushes every item; returns how many failed (each failure is logged).</summary>
	public static int FlushAll(IEnumerable<IFlushesDrafts> items)
	{
		var failed = 0;
		foreach (var item in items)
		{
			try
			{
				item.FlushDrafts();
			}
			catch (Exception ex)
			{
				failed++;
				_log.Warning(ex, "DraftFlusher: flushing the drafts of {Item} failed", item.GetType().Name);
			}
		}
		return failed;
	}
}
