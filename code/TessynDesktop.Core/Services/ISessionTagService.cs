using System;
using System.Collections.Generic;
using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <summary>
/// Owns Session Tags (FR.24): the declared tags of every session, the Linear workspace map, and the
/// name-derived tags computed on read. A Singleton: it caches ledgers and serialises writes. The UI reaches
/// it only through <see cref="IDaemonClient"/>.
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface ISessionTagService
{
	/// <summary>Raised once after a change (FR.24.7): the node id for a tag change, null when a Linear team was learned. May fire on any thread.</summary>
	event EventHandler<SessionTagsChangedEventArgs>? SessionTagsChanged;

	/// <summary>
	/// Applies a <c>tag_session</c> call (FR.24.4): <paramref name="add"/> entries first, then <paramref name="remove"/>,
	/// each reported on its own; an invalid entry never stops the others. Full Linear URLs in <paramref name="add"/>
	/// teach the workspace map (FR.24.3). Writes the tag file only when declared tags changed.
	/// </summary>
	SessionTagEditResultModel Edit(SessionNodeModel node, IReadOnlyList<string> add, IReadOnlyList<string> remove);

	/// <summary>Name-derived plus declared tags in FR.24.7 order (a name-derived tag hides a declared one with the same key). Returns copies.</summary>
	IReadOnlyList<SessionTagModel> GetTags(string nodeId, string? sessionName);

	/// <summary>True once per node per app run (FR.24.6); the state is in memory only.</summary>
	bool TakeTagGuidanceDue(string nodeId);
}
