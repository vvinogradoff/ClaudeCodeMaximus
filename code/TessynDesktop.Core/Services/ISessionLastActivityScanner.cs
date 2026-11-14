using System.Collections.Generic;
using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <summary>Batch reader of per-session last-activity timestamps. Thread-safe; touches no UI state.</summary>
/// <remarks>Created by Claude</remarks>
public interface ISessionLastActivityScanner
{
	/// <summary>Returns one result per input file name, in input order. A missing or unreadable file yields null timestamps.</summary>
	IReadOnlyList<SessionLastActivityModel> Scan(IReadOnlyList<string> sessionFileNames);
}
