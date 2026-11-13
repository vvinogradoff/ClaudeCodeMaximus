using System;
using System.Collections.Generic;
using System.IO;
using TessynDesktop.Models;
using TessynDesktop.Services;

namespace TessynDesktop.Tests.Services;

/// <summary>A real <see cref="SessionTagService"/> over a temp-dir store; disposing deletes the temp dir.</summary>
/// <remarks>Created by Claude</remarks>
internal sealed class TagTestEnv : IDisposable
{
	public string Root { get; }
	public ManualTimeProvider Clock { get; } = new();
	public SessionTagStore Store { get; }
	public SessionTagService Service { get; }

	public TagTestEnv()
	{
		Root    = Path.Combine(Path.GetTempPath(), $"tag-tests-{Guid.NewGuid():N}");
		Store   = new SessionTagStore(Root);
		Service = new SessionTagService(Store, Clock);
	}

	/// <summary>A second service over the same files (an app restart).</summary>
	public SessionTagService NewServiceOverSameRoot() => new(new SessionTagStore(Root), Clock);

	/// <summary>A service that is never expected to touch disk (for tests that only need the constructor argument).</summary>
	public static SessionTagService Unused() =>
		new(new SessionTagStore(Path.Combine(Path.GetTempPath(), $"tag-unused-{Guid.NewGuid():N}")));

	public static SessionNodeModel Node(string id = "node-1", string name = "Session", int depth = 0) => new()
	{
		NodeId             = id,
		Name               = name,
		FileName           = $"{id}.txt",
		WorkingDirectory   = @"C:\Proj\" + id,
		OrchestrationDepth = depth,
	};

	public void Dispose()
	{
		if (Directory.Exists(Root))
			Directory.Delete(Root, recursive: true);
	}
}

/// <summary><see cref="ISessionTagService"/> fake that records calls, for delegation tests (TC.TG.14, TC.TG.18).</summary>
/// <remarks>Created by Claude</remarks>
internal sealed class FakeSessionTagService : ISessionTagService
{
	public event EventHandler<SessionTagsChangedEventArgs>? SessionTagsChanged;

	public List<SessionTagModel> Tags { get; set; } = new();
	/// <summary>Per-node tags; a node with an entry here wins over <see cref="Tags"/> (index tests, TC.TG.35).</summary>
	public Dictionary<string, List<SessionTagModel>> TagsByNode { get; } = new();
	public (string nodeId, string? name)? LastGetTags { get; private set; }
	public int TakeCalls { get; private set; }
	public HashSet<string> Taken { get; } = new();

	public int SubscriberCount => SessionTagsChanged?.GetInvocationList().Length ?? 0;

	public void Raise(string? nodeId) => SessionTagsChanged?.Invoke(this, new SessionTagsChangedEventArgs(nodeId));

	public SessionTagEditResultModel Edit(SessionNodeModel node, IReadOnlyList<string> add, IReadOnlyList<string> remove) =>
		throw new NotImplementedException();

	public IReadOnlyList<SessionTagModel> GetTags(string nodeId, string? sessionName)
	{
		LastGetTags = (nodeId, sessionName);
		return TagsByNode.TryGetValue(nodeId, out var own) ? own : Tags;
	}

	public SessionTagParseResultModel Parse(string entry) => throw new NotImplementedException();

	public bool TakeTagGuidanceDue(string nodeId)
	{
		TakeCalls++;
		return Taken.Add(nodeId);
	}
}
