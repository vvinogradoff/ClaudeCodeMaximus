using System.Collections.Generic;
using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <summary>
/// Builds the Session Tag Index (FR.24.13) from a tree snapshot and <see cref="ISessionTagService.GetTags"/>.
/// The one place that wires <see cref="SessionTagIndexBuilder"/> to the registry and the tag service, shared by
/// <c>IDaemonClient.GetSessionTagIndex</c> and the cmx tag tools (FR.24.15).
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class SessionTagIndexProvider
{
	private readonly ISessionRegistry   _registry;
	private readonly ISessionTagService _tags;

	public SessionTagIndexProvider(ISessionRegistry registry, ISessionTagService tags)
	{
		_registry = registry;
		_tags     = tags;
	}

	/// <summary>The tree is snapshotted briefly under the registry lock; tags (which may read files) are read outside it.</summary>
	public IReadOnlyList<SessionTagUsageModel> GetIndex(string? directoryPath) =>
		Build(_registry.SnapshotSessionsForTagIndex(), directoryPath);

	/// <summary>The index of an already taken snapshot; null <paramref name="directoryPath"/> means every directory.</summary>
	public IReadOnlyList<SessionTagUsageModel> Build(IReadOnlyList<SessionTagIndexSessionModel> snapshot, string? directoryPath) =>
		SessionTagIndexBuilder.Build(snapshot, s => _tags.GetTags(s.NodeId, s.Name), directoryPath);
}
