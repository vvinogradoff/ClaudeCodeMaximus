using System.Collections.Generic;

namespace TessynDesktop.Services;

/// <summary>
/// Every Claude session id the Tessyn tree refers to (current, external and prior ids of every node). The session
/// index uses it so its purge never touches sessions the tree still owns, even when Claude removed their JSONL.
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface ITreeSessionIdSource
{
	/// <summary>A snapshot taken under the settings lock.</summary>
	IReadOnlySet<string> GetTreeSessionIds();
}
