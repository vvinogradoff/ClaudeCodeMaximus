using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Services;

namespace TessynDesktop.Tests.Services;

/// <summary>A session file service with settable entries and stamps that counts how often a file is read (session.entries cache tests).</summary>
/// <remarks>Created by Claude</remarks>
internal sealed class StubFileService : ISessionFileService
{
	private readonly Dictionary<string, List<SessionEntryModel>> _entries = new();
	private readonly Dictionary<string, SessionFileStampModel> _stamps = new();
	private int _created;

	public int ReadCalls { get; private set; }

	public event EventHandler<SessionFileRewrittenEventArgs>? SessionFileRewritten;
	public event EventHandler<SessionEntryAppendedEventArgs>? EntryAppended { add { } remove { } }
	public event EventHandler<SessionTurnCompletedEventArgs>? TurnCompleted { add { } remove { } }

	/// <summary>Sets the file's entries and stamp (a new <paramref name="length"/> or <paramref name="lastWrite"/> invalidates caches).</summary>
	public void Set(string fileName, IEnumerable<SessionEntryModel> entries, long length = 1, DateTimeOffset? lastWrite = null)
	{
		_entries[fileName] = [.. entries];
		_stamps[fileName]  = new SessionFileStampModel(length, lastWrite ?? new DateTimeOffset(2026, 11, 12, 10, 0, 0, TimeSpan.Zero));
	}

	public void Remove(string fileName)
	{
		_entries.Remove(fileName);
		_stamps.Remove(fileName);
	}

	public void RaiseRewritten(string fileName) =>
		SessionFileRewritten?.Invoke(this, new SessionFileRewrittenEventArgs { FileName = fileName });

	public SessionFileStampModel? GetFileStamp(string fileName) => _stamps.GetValueOrDefault(fileName);

	public IReadOnlyList<SessionEntryModel> ReadEntries(string fileName)
	{
		ReadCalls++;
		return _entries.TryGetValue(fileName, out var list) ? [.. list] : [];
	}

	public IReadOnlyList<SessionEntryModel> ReadTailEntries(string fileName, int maxBytes) => [];
	public IReadOnlyList<SessionEntryModel> ReadEntriesFrom(string fileName, long startOffset) => [];
	public string CreateSessionFile() => $"stub-{++_created}.txt";
	public void NotifyTurnCompleted(string fileName, bool cancelled, string? errorText) { }
	public void AppendMessage(string fileName, string role, string content, string? profileName = null, string? modelId = null, string? effort = null) { }
	public void AppendCompactionSeparator(string fileName) { }
	public bool SessionFileExists(string fileName) => _stamps.ContainsKey(fileName);
	public DateTimeOffset? GetLastWriteTimeUtc(string fileName) => _stamps.TryGetValue(fileName, out var s) ? s.LastWriteUtc : null;
	public void DeleteSessionFile(string fileName) { }
	public string GetFullPath(string fileName) => fileName;
	public void RewriteSessionFile(string fileName, string content) { }
	public void WriteSessionFile(string fileName, IReadOnlyList<SessionEntryModel> entries) { }
	public int RepairCorruptedCompactions() => 0;
}

/// <summary>Streaming process manager that tracks which sessions have a live process and which were stopped.</summary>
/// <remarks>Created by Claude</remarks>
internal sealed class StubStreamingManager : IClaudeStreamingSessionManager
{
	public HashSet<string> Alive { get; } = new();
	public List<string> Stopped { get; } = new();

	public bool EnsureStarted(string nodeId, StreamingSpawnParams spawnParams, Action<ClaudeStreamEvent> onEvent) => throw new NotImplementedException();
	public Task SendTurnAsync(string nodeId, string userMessage, CancellationToken cancellationToken, Action<ClaudeStreamEvent>? onTurnEvent = null) => throw new NotImplementedException();
	public bool IsAlive(string nodeId) => Alive.Contains(nodeId);

	public void Stop(string nodeId)
	{
		Stopped.Add(nodeId);
		Alive.Remove(nodeId);
	}

	public void TerminateAll() { }
	public event Action<string>? ProcessExited { add { } remove { } }
}

/// <summary>Turn service that records every run, optionally holding the turn open until <see cref="Release"/>.</summary>
/// <remarks>Created by Claude</remarks>
internal sealed class StubTurnService : ISessionTurnService
{
	private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

	public List<(SessionNodeModel Node, string Prompt, TurnSource Source, TurnOptionsModel? Options)> Runs { get; } = new();
	public SemaphoreSlim Started { get; } = new(0);
	public bool HoldTurns { get; set; }
	public bool ThrowOnRun { get; set; }
	public bool CancelResult { get; set; }
	public List<string> Cancelled { get; } = new();

	public void Release() => _gate.TrySetResult();

	public SemaphoreSlim GetTurnLock(string nodeId) => new(1, 1);

	public Task<TurnResultModel> RunTurnAsync(SessionNodeModel node, string prompt, TurnSource source, CancellationToken cancellationToken = default) =>
		RunTurnAsync(node, prompt, source, null, cancellationToken);

	public async Task<TurnResultModel> RunTurnAsync(SessionNodeModel node, string prompt, TurnSource source, TurnOptionsModel? options, CancellationToken cancellationToken = default)
	{
		lock (Runs)
			Runs.Add((node, prompt, source, options));

		Started.Release();
		if (HoldTurns)
			await _gate.Task;

		if (ThrowOnRun)
			throw new InvalidOperationException("turn failed");

		return new TurnResultModel(AssistantText: "ok", SessionId: null);
	}

	public bool CancelTurn(string nodeId)
	{
		Cancelled.Add(nodeId);
		return CancelResult;
	}

	public string? ResolveProfileName(SessionNodeModel node) => null;
}

/// <summary>Records sends and an ordering log shared with other fakes.</summary>
/// <remarks>Created by Claude</remarks>
internal sealed class RecordingSendService : ISessionSendService
{
	public List<(SessionNodeModel Node, string Text, TurnOptionsModel? Options)> Sends { get; } = new();
	public Action? OnSend { get; set; }
	public bool QueuedResult { get; set; }

	public bool Send(SessionNodeModel node, string text, TurnOptionsModel? options)
	{
		OnSend?.Invoke();
		Sends.Add((node, text, options));
		return QueuedResult;
	}
}

/// <summary>Returns a fixed tag list for every session.</summary>
/// <remarks>Created by Claude</remarks>
internal sealed class StubTagService : ISessionTagService
{
	public event EventHandler<SessionTagsChangedEventArgs>? SessionTagsChanged { add { } remove { } }
	public List<SessionTagModel> Tags { get; } = new();

	public SessionTagEditResultModel Edit(SessionNodeModel node, IReadOnlyList<string> add, IReadOnlyList<string> remove) => throw new NotImplementedException();
	public IReadOnlyList<SessionTagModel> GetTags(string nodeId, string? sessionName) => Tags;
	public SessionTagParseResultModel Parse(string entry) => throw new NotImplementedException();
	public bool TakeTagGuidanceDue(string nodeId) => false;
}

/// <summary>Helpers shared by the remote session tests.</summary>
/// <remarks>Created by Claude</remarks>
internal static class RemoteTestHelpers
{
	public static readonly DateTimeOffset T0 = new(2026, 11, 12, 10, 0, 0, TimeSpan.Zero);

	public static SessionEntryModel Entry(string role, string content, int minute = 0, string? profile = null, string? model = null, string? effort = null) =>
		new()
		{
			Timestamp   = T0.AddMinutes(minute),
			Role        = role,
			Content     = content,
			ProfileName = profile,
			ModelId     = model,
			Effort      = effort,
		};

	public static SessionEntryModel User(string content, int minute = 0, string? profile = null) =>
		Entry(Constants.SessionFile.RoleUser, content, minute, profile: profile);

	public static SessionEntryModel Assistant(string content, int minute = 0, string? model = null, string? effort = null) =>
		Entry(Constants.SessionFile.RoleAssistant, content, minute, model: model, effort: effort);

	public static JsonObject Params(string json) => JsonNode.Parse(json)!.AsObject();

	/// <summary>Invokes a method of <paramref name="module"/> by name and returns its result as the wire JSON.</summary>
	public static async Task<JsonNode?> CallAsync(IRemoteRpcModule module, string method, string json)
	{
		var registry = new TdcpMethodRegistry();
		module.Register(registry);
		Assert_True(registry.TryGet(method, out var entry), $"method {method} is not registered");
		Assert_True(entry.RemoteCallable, $"method {method} is not remote callable");

		var result = await entry.Handler(JsonNode.Parse(json), CancellationToken.None);
		return result == null ? null : JsonSerializer.SerializeToNode(result, result.GetType(), TdcpJsonOptions.Default);
	}

	/// <summary>Invokes a method and returns the raw result object.</summary>
	public static async Task<object?> CallRawAsync(IRemoteRpcModule module, string method, string json)
	{
		var registry = new TdcpMethodRegistry();
		module.Register(registry);
		registry.TryGet(method, out var entry);
		return await entry.Handler(JsonNode.Parse(json), CancellationToken.None);
	}

	private static void Assert_True(bool condition, string message)
	{
		if (!condition)
			throw new InvalidOperationException(message);
	}
}

/// <summary>
/// A real registry over temp-dir settings plus the Core pieces the session module needs, wired with fakes for the process side.
/// Disposing deletes the temp dir.
/// </summary>
/// <remarks>Created by Claude</remarks>
internal sealed class RemoteSessionEnv : IDisposable
{
	private readonly string _tempDir;

	public string TempDir => _tempDir;
	public AppSettingsService Settings { get; }
	public SessionRegistry Registry { get; }
	public StubFileService Files { get; } = new();
	public SessionEpochTracker Epochs { get; }
	public SessionEntryReader Reader { get; }
	public StubStreamingManager Streaming { get; } = new();
	public StubTurnService Turns { get; } = new();
	public FakeProfileServiceForTurnService ProfileService { get; } = new();
	public FakeModelServiceForTurnService Models { get; } = new();
	public ProfileLabelService Labels { get; }
	public TurnOptionsResolver Options { get; }
	public SessionSendService Send { get; }
	public StubTagService Tags { get; } = new();
	public FakeAttentionService Attention { get; } = new();
	public FakeActivityService Activity { get; } = new();
	public SessionRpcModule Module { get; }
	public List<string> SettingsChanged { get; } = new();

	public DirectoryNodeModel Directory { get; }
	public SessionNodeModel Session { get; }

	public RemoteSessionEnv()
	{
		_tempDir = Path.Combine(Path.GetTempPath(), "cm_test_remote_session_" + Path.GetRandomFileName());
		System.IO.Directory.CreateDirectory(_tempDir);

		Settings = new AppSettingsService(_tempDir);
		Registry = new SessionRegistry(Settings, Files, new FakeDirectoryLabelService(), new FakeClaudeSessionStatusService());
		Registry.SessionSettingsChanged += (_, e) => SettingsChanged.Add(e.NodeId);

		Epochs  = new SessionEpochTracker(Files);
		Reader  = new SessionEntryReader(Files, Epochs);
		Labels  = new ProfileLabelService(ProfileService, Settings);
		Options = new TurnOptionsResolver(Models, Settings, Labels);
		Send    = new SessionSendService(Turns, Registry);

		Directory = Registry.AddDirectory(Path.Combine(_tempDir, "proj"));
		Session   = Registry.AddSession(Directory, "Main session");

		Module = new SessionRpcModule(
			Registry, Settings,
			new SessionStateBuilder(Registry, Streaming, Attention, Activity),
			Tags, Attention, Reader, Epochs, Models, Labels, Send, Options, Turns, Streaming);
	}

	public Task<JsonNode?> CallAsync(string method, string json) => RemoteTestHelpers.CallAsync(Module, method, json);

	public string NodeJson(string extra = "") =>
		$"{{\"nodeId\":\"{Session.NodeId}\"{(extra.Length == 0 ? string.Empty : "," + extra)}}}";

	public void Dispose()
	{
		Epochs.Dispose();
		if (System.IO.Directory.Exists(_tempDir))
			System.IO.Directory.Delete(_tempDir, recursive: true);
	}
}
