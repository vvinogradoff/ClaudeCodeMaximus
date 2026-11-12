using System;
using System.Collections.Generic;
using System.IO;
using TessynDesktop.Models;
using TessynDesktop.Services;

namespace TessynDesktop.Tests.Services;

/// <summary>Session file fake that records every appended entry (FR.23.8 SYSTEM entries) and nothing else.</summary>
/// <remarks>Created by Claude</remarks>
internal sealed class RecordingSessionFileService : ISessionFileService
{
    public event EventHandler<SessionFileRewrittenEventArgs>? SessionFileRewritten { add { } remove { } }
    public IReadOnlyList<SessionEntryModel> ReadTailEntries(string fileName, int maxBytes) => Array.Empty<SessionEntryModel>();
    public SessionFileStampModel? GetFileStamp(string fileName) => null;
	public List<(string fileName, string role, string content)> Appended { get; } = new();

	public event EventHandler<SessionEntryAppendedEventArgs>? EntryAppended { add { } remove { } }
	public event EventHandler<SessionTurnCompletedEventArgs>? TurnCompleted { add { } remove { } }

	public void AppendMessage(string fileName, string role, string content, string? profileName = null, string? modelId = null, string? effort = null)
	{
		lock (Appended)
			Appended.Add((fileName, role, content));
	}

	public void NotifyTurnCompleted(string fileName, bool cancelled, string? errorText) { }
	public string CreateSessionFile() => throw new NotImplementedException();
	public void AppendCompactionSeparator(string fileName) { }
	public IReadOnlyList<SessionEntryModel> ReadEntries(string fileName) => Array.Empty<SessionEntryModel>();
	public bool SessionFileExists(string fileName) => true;
	public DateTimeOffset? GetLastWriteTimeUtc(string fileName) => null;
	public void DeleteSessionFile(string fileName) { }
	public string GetFullPath(string fileName) => fileName;
	public void RewriteSessionFile(string fileName, string content) { }
	public void WriteSessionFile(string fileName, IReadOnlyList<SessionEntryModel> entries) { }
	public int RepairCorruptedCompactions() => 0;
}

/// <summary>Clock the test advances by hand, so "UpdatedUtc advanced" and closure ordering are deterministic.</summary>
/// <remarks>Created by Claude</remarks>
internal sealed class ManualTimeProvider : TimeProvider
{
	private DateTimeOffset _now = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);

	public override DateTimeOffset GetUtcNow() => _now;

	public void Advance(TimeSpan by) => _now += by;
}

/// <summary>
/// A real <see cref="AttentionService"/> over a temp-dir store and a recording session file fake.
/// Disposing deletes the temp dir.
/// </summary>
/// <remarks>Created by Claude</remarks>
internal sealed class AttentionTestEnv : IDisposable
{
	public string Root { get; }
	public RecordingSessionFileService Files { get; } = new();
	public ManualTimeProvider Clock { get; } = new();
	public AttentionLedgerStore Store { get; }
	public AttentionService Service { get; }

	public AttentionTestEnv()
	{
		Root    = Path.Combine(Path.GetTempPath(), $"attention-tests-{Guid.NewGuid():N}");
		Store   = new AttentionLedgerStore(Root);
		Service = new AttentionService(Store, Files, Clock);
	}

	/// <summary>A second service over the same files (a "restart").</summary>
	public AttentionService NewServiceOverSameRoot() => new(new AttentionLedgerStore(Root), Files, Clock);

	public static SessionNodeModel Node(string id = "node-1", int depth = 0) => new()
	{
		NodeId             = id,
		Name               = $"Session-{id}",
		FileName           = $"{id}.txt",
		WorkingDirectory   = @"C:\Proj\" + id,
		OrchestrationDepth = depth,
	};

	public static AttentionPostRequestModel Req(
		string title, AttentionKind kind = AttentionKind.Ask, string? key = null, string body = "",
		string[]? options = null, string? recommendation = null) => new()
	{
		Kind           = kind,
		Title          = title,
		Body           = body,
		Key            = key,
		Options        = options ?? [],
		Recommendation = recommendation,
	};

	public void Dispose()
	{
		if (Directory.Exists(Root))
			Directory.Delete(Root, recursive: true);
	}
}

/// <summary>
/// <see cref="IAttentionService"/> fake that records calls, for delegation tests of the daemon client (TC.FV.19).
/// </summary>
/// <remarks>Created by Claude</remarks>
internal sealed class FakeAttentionService : IAttentionService
{
	public event EventHandler<AttentionChangedEventArgs>? AttentionChanged;

	public List<AttentionItemModel> Items { get; set; } = new();
	public AttentionSummaryModel Summary { get; set; } = new();
	public List<AttentionItemModel> Closures { get; set; } = new();

	public string? LastItemsNodeId { get; private set; }
	public string? LastSummaryNodeId { get; private set; }
	public (string nodeId, IReadOnlyList<AttentionAnswerModel> answers)? LastMarkAnswered { get; private set; }
	public (string nodeId, string itemId)? LastCloseByUser { get; private set; }
	public string? LastTakeNodeId { get; private set; }

	public int SubscriberCount => AttentionChanged?.GetInvocationList().Length ?? 0;

	public void Raise(string nodeId) => AttentionChanged?.Invoke(this, new AttentionChangedEventArgs(nodeId));

	public IReadOnlyList<AttentionPostOutcomeModel> Post(SessionNodeModel node, IReadOnlyList<AttentionPostRequestModel> requests) =>
		throw new NotImplementedException();

	public AttentionResolveResultModel ResolveBySession(SessionNodeModel node, string itemId, AttentionStatus status, string? note) =>
		throw new NotImplementedException();

	public IReadOnlyList<AttentionItemModel> GetItems(string nodeId)
	{
		LastItemsNodeId = nodeId;
		return Items;
	}

	/// <summary>Per-node summaries; a node with an entry here wins over <see cref="Summary"/> (remote list tests).</summary>
	public Dictionary<string, AttentionSummaryModel> SummaryByNode { get; } = new();

	public AttentionSummaryModel GetSummary(string nodeId)
	{
		LastSummaryNodeId = nodeId;
		return SummaryByNode.TryGetValue(nodeId, out var own) ? own : Summary;
	}

	public void MarkAnsweredByUser(string nodeId, IReadOnlyList<AttentionAnswerModel> answers) =>
		LastMarkAnswered = (nodeId, answers);

	public bool CloseByUser(SessionNodeModel node, string itemId)
	{
		LastCloseByUser = (node.NodeId, itemId);
		return true;
	}

	public IReadOnlyList<AttentionItemModel> TakeUnreportedUserClosures(string nodeId)
	{
		LastTakeNodeId = nodeId;
		return Closures;
	}
}
