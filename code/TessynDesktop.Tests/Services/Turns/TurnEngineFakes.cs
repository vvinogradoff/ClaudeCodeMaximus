using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Models.Agent;
using TessynDesktop.Services;

namespace TessynDesktop.Tests.Services.Turns;

/// <summary>One launch of a one-shot claude process, as the engine asked for it.</summary>
/// <remarks>Created by Claude</remarks>
internal sealed record ProcessCall(
	string WorkingDirectory, string ClaudePath, string? SessionId, string Message, string? Model, string? ProfileConfigDir,
	string? Effort, string? McpConfigPath, string? OllamaBaseUrl, bool DisableTools, bool Chrome, bool DisallowNativeScheduling,
	LocalToolSpec? LocalToolSpec, bool StrictMcpConfig);

/// <summary>Process manager whose launches follow a script: the n-th launch runs the n-th behaviour (nothing when the script is exhausted).</summary>
/// <remarks>Created by Claude</remarks>
internal sealed class ScriptedProcessManager : IClaudeProcessManager
{
	private readonly Queue<Func<ProcessCall, Action<ClaudeStreamEvent>, CancellationToken, Task>> _script = new();

	public int ActiveProcessCount => 0;
	public List<ProcessCall> Calls { get; } = [];

	/// <summary>Runs just before a launch is recorded, for tests that look at the world while the "process" starts.</summary>
	public Action<ProcessCall>? OnLaunch { get; set; }

	public void Script(Func<ProcessCall, Action<ClaudeStreamEvent>, CancellationToken, Task> behaviour)
	{
		lock (_script) _script.Enqueue(behaviour);
	}

	/// <summary>The next launch emits <paramref name="events"/> and exits.</summary>
	public void ScriptEvents(params ClaudeStreamEvent[] events) =>
		Script((_, onEvent, _) =>
		{
			foreach (var evt in events)
				onEvent(evt);
			return Task.CompletedTask;
		});

	public async Task SendMessageAsync(
		string workingDirectory, string claudePath, string? sessionId, string userMessage,
		Action<ClaudeStreamEvent> onEvent, string? model = null, string? profileConfigDir = null,
		string? effort = null, string? mcpConfigPath = null, string? ollamaBaseUrl = null,
		bool disableTools = false, bool chrome = false, bool disallowNativeScheduling = false,
		LocalToolSpec? localToolSpec = null, bool strictMcpConfig = false,
		CancellationToken cancellationToken = default)
	{
		var call = new ProcessCall(workingDirectory, claudePath, sessionId, userMessage, model, profileConfigDir, effort, mcpConfigPath,
			ollamaBaseUrl, disableTools, chrome, disallowNativeScheduling, localToolSpec, strictMcpConfig);

		OnLaunch?.Invoke(call);

		Func<ProcessCall, Action<ClaudeStreamEvent>, CancellationToken, Task>? behaviour = null;
		lock (Calls)
		{
			Calls.Add(call);
			lock (_script)
			{
				if (_script.Count > 0)
					behaviour = _script.Dequeue();
			}
		}

		if (behaviour != null)
			await behaviour(call, onEvent, cancellationToken);
	}

	public Task<string?> RunPrintModeAsync(string claudePath, string prompt, string? model = null, string? profileConfigDir = null,
		int timeoutMs = 60000, CancellationToken cancellationToken = default) => throw new NotSupportedException();

	public void TerminateAll() { }
}

/// <summary>Streaming manager with a script per <c>SendTurnAsync</c>, recording spawns and writes; the primary event callback is kept like the real one.</summary>
/// <remarks>Created by Claude</remarks>
internal sealed class ScriptedStreamingManager : IClaudeStreamingSessionManager
{
	private readonly Queue<Func<Action<ClaudeStreamEvent>, CancellationToken, Task>> _script = new();
	private readonly HashSet<string> _alive = [];
	private Action<ClaudeStreamEvent>? _primary;

	public List<(string NodeId, StreamingSpawnParams Params)> EnsureCalls { get; } = [];
	public List<(string NodeId, string Message)> Sends { get; } = [];
	public List<string> Stopped { get; } = [];

	public void Script(Func<Action<ClaudeStreamEvent>, CancellationToken, Task> behaviour) => _script.Enqueue(behaviour);

	public void ScriptEvents(params ClaudeStreamEvent[] events) =>
		Script((primary, _) =>
		{
			foreach (var evt in events)
				primary(evt);
			return Task.CompletedTask;
		});

	public bool EnsureStarted(string nodeId, StreamingSpawnParams spawnParams, Action<ClaudeStreamEvent> onEvent)
	{
		EnsureCalls.Add((nodeId, spawnParams));
		_primary = onEvent;
		return _alive.Add(nodeId);
	}

	public async Task SendTurnAsync(string nodeId, string userMessage, CancellationToken cancellationToken, Action<ClaudeStreamEvent>? onTurnEvent = null)
	{
		Sends.Add((nodeId, userMessage));
		if (_script.Count > 0)
			await _script.Dequeue()(_primary!, cancellationToken);
	}

	public bool IsAlive(string nodeId) => _alive.Contains(nodeId);

	public void Stop(string nodeId)
	{
		Stopped.Add(nodeId);
		_alive.Remove(nodeId);
	}

	public void TerminateAll() { }
	public event Action<string>? ProcessExited { add { } remove { } }
}

/// <summary>
/// File service keeping real entries in memory and one chronological log shared with the other fakes, so tests can assert what
/// was written when. <see cref="NotifyTurnCompleted"/> also records whether the node's turn lock was still held.
/// </summary>
/// <remarks>Created by Claude</remarks>
internal sealed class InMemorySessionFileService : ISessionFileService
{
	private readonly object _gate = new();
	private readonly List<SessionEntryModel> _entries = [];

	public List<string> Log { get; } = [];
	public List<(string FileName, bool Cancelled, string? ErrorText, int LockCount)> Completions { get; } = [];
	public int CompactionSeparators { get; private set; }

	/// <summary>Reports the current count of the turn lock a completion is raised under (0 = still held).</summary>
	public Func<int>? LockCount { get; set; }

	public event EventHandler<SessionEntryAppendedEventArgs>? EntryAppended { add { } remove { } }
	public event EventHandler<SessionFileRewrittenEventArgs>? SessionFileRewritten { add { } remove { } }
	public event EventHandler<SessionFileDeletedEventArgs>? SessionFileDeleted { add { } remove { } }
	public event EventHandler<SessionTurnCompletedEventArgs>? TurnCompleted { add { } remove { } }

	public void Seed(string role, string content)
	{
		lock (_gate)
			_entries.Add(new SessionEntryModel { Timestamp = DateTimeOffset.UtcNow, Role = role, Content = content });
	}

	public IReadOnlyList<SessionEntryModel> Entries
	{
		get { lock (_gate) return [.. _entries]; }
	}

	public void AppendMessage(string fileName, string role, string content, string? profileName = null, string? modelId = null, string? effort = null)
	{
		lock (_gate)
		{
			_entries.Add(new SessionEntryModel
			{
				Timestamp   = DateTimeOffset.UtcNow,
				Role        = role,
				Content     = content,
				ProfileName = profileName,
				ModelId     = modelId,
				Effort      = effort,
			});
			Log.Add($"append:{role}:{content}");
		}
	}

	public void AppendCompactionSeparator(string fileName)
	{
		lock (_gate)
		{
			CompactionSeparators++;
			Log.Add("append:COMPACTION");
		}
	}

	public void NotifyTurnCompleted(string fileName, bool cancelled, string? errorText)
	{
		lock (_gate)
		{
			Completions.Add((fileName, cancelled, errorText, LockCount?.Invoke() ?? -1));
			Log.Add($"completed:cancelled={cancelled}:error={errorText ?? "null"}");
		}
	}

	public IReadOnlyList<SessionEntryModel> ReadEntries(string fileName) => Entries;

	public IReadOnlyList<SessionEntryModel> ReadTailEntries(string fileName, int maxBytes) => [];
	public IReadOnlyList<SessionEntryModel> ReadEntriesFrom(string fileName, long startOffset) => [];
	public SessionFileStampModel? GetFileStamp(string fileName) => null;
	public string CreateSessionFile() => throw new NotSupportedException();
	public bool SessionFileExists(string fileName) => true;
	public DateTimeOffset? GetLastWriteTimeUtc(string fileName) => null;
	public void DeleteSessionFile(string fileName) { }
	public string GetFullPath(string fileName) => fileName;
	public void RewriteSessionFile(string fileName, string content) { }
	public void WriteSessionFile(string fileName, IReadOnlyList<SessionEntryModel> entries) { }
	public int RepairCorruptedCompactions() => 0;
}

/// <summary>Turn service that only hands out one shared lock per node (what <see cref="TurnEngine"/> needs of it).</summary>
/// <remarks>Created by Claude</remarks>
internal sealed class SharedLockTurnService : ISessionTurnService
{
	private readonly Dictionary<string, SemaphoreSlim> _locks = [];

	public SemaphoreSlim GetTurnLock(string nodeId)
	{
		lock (_locks)
		{
			if (!_locks.TryGetValue(nodeId, out var turnLock))
				_locks[nodeId] = turnLock = new SemaphoreSlim(1, 1);
			return turnLock;
		}
	}

	public Task<TurnResultModel> RunTurnAsync(SessionNodeModel node, string prompt, TurnSource source, CancellationToken cancellationToken = default) =>
		throw new NotSupportedException();

	public Task<TurnResultModel> RunTurnAsync(SessionNodeModel node, string prompt, TurnSource source, TurnOptionsModel? options,
		CancellationToken cancellationToken = default) => throw new NotSupportedException();

	public bool CancelTurn(string nodeId) => false;
	public string? ResolveProfileName(SessionNodeModel node) => null;
}

/// <summary>Records the cross-profile adoption checks of the engine.</summary>
/// <remarks>Created by Claude</remarks>
internal sealed class RecordingSessionStatusService : IClaudeSessionStatusService
{
	public List<(string WorkingDirectory, string SessionId, string? ProfileConfigDir)> Adoptions { get; } = [];

	public bool TryAdoptSessionFromOtherProfile(string workingDirectory, string claudeSessionId, string? activeProfileConfigDir)
	{
		Adoptions.Add((workingDirectory, claudeSessionId, activeProfileConfigDir));
		return true;
	}

	public bool IsSessionResumable(string workingDirectory, string claudeSessionId, IReadOnlyList<string>? projectsDirs = null) => throw new NotSupportedException();
	public IReadOnlyList<string> GetAllProjectsDirs() => throw new NotSupportedException();
	public IReadOnlyList<bool> AreSessionsResumable(IReadOnlyList<SessionResumabilityRequest> requests, IReadOnlyList<string>? projectsDirs = null) =>
		throw new NotSupportedException();
}

/// <summary>MCP server fake that returns a recognisable config path and records the requests.</summary>
/// <remarks>Created by Claude</remarks>
internal sealed class RecordingMcpServer : IAgentMcpServer
{
	public List<(string NodeId, string AgentToken, bool? IncludeCmx, IReadOnlyList<string>? ExternalIds)> Requests { get; } = [];

	/// <summary>When set, every config request throws it.</summary>
	public Exception? Failure { get; set; }

	public int Port => 0;
	public void Start() { }
	public void Stop() { }

	public Task<string?> EnsureConfigFileAsync(string nodeId, string agentToken)
	{
		if (Failure != null)
			throw Failure;

		Requests.Add((nodeId, agentToken, null, null));
		return Task.FromResult<string?>($@"C:\mcp\{nodeId}.json");
	}

	public Task<string?> EnsureConfigFileAsync(string nodeId, string agentToken, bool includeCmx, IReadOnlyList<string> externalServerIds)
	{
		if (Failure != null)
			throw Failure;

		Requests.Add((nodeId, agentToken, includeCmx, externalServerIds));
		return Task.FromResult<string?>($@"C:\mcp\{nodeId}.local.json");
	}
}

/// <summary>Telegram bridge fake counting the lazy topic requests.</summary>
/// <remarks>Created by Claude</remarks>
internal sealed class RecordingTelegramBridge : ITelegramBridgeService
{
	public List<string> TopicRequests { get; } = [];

	public void Start() { }
	public void Stop() { }

	public Task EnsureTopicForSessionAsync(SessionNodeModel node)
	{
		TopicRequests.Add(node.NodeId);
		return Task.CompletedTask;
	}

	public Task<bool> NotifyAsync(string nodeId, string message) => throw new NotSupportedException();
}

/// <summary>Observer that records every callback as a readable line (plus the first-assistant metadata) and can run a hook after the response.</summary>
/// <remarks>Created by Claude</remarks>
internal sealed class RecordingTurnObserver : ITurnObserver
{
	private readonly object _gate = new();

	public List<string> Calls { get; } = [];
	public List<(string Text, string? ModelId, string? Effort)> Assistants { get; } = [];
	public Func<Task>? AfterResponse { get; set; }

	private void Add(string line)
	{
		lock (_gate) Calls.Add(line);
	}

	public void UserEntryAppended(DateTimeOffset timestamp, string? profileName) => Add($"user:{profileName ?? "null"}");

	public void AssistantEntryAppended(string text, DateTimeOffset timestamp, string? modelId, string? effort)
	{
		lock (_gate) Assistants.Add((text, modelId, effort));
		Add($"assistant:{text}");
	}

	public void CompactionAppended(DateTimeOffset timestamp) => Add("compaction");
	public void ProgressText(string text, DateTimeOffset timestamp) => Add($"progress:{text}");
	public void SystemError(string text, DateTimeOffset timestamp) => Add($"systemError:{text}");
	public void ResultError(string text, DateTimeOffset timestamp) => Add($"resultError:{text}");
	public void ResultEnded() => Add("resultEnded");
	public void SessionIdCaptured() => Add("sessionId");
	public void AuthErrorChanged(bool hasAuthError) => Add($"authError:{hasAuthError}");
	public void StreamingProcessStarted() => Add("streamingStarted");
	public void ContextRetrying() => Add("contextRetrying");
	public void Interrupted(DateTimeOffset timestamp) => Add("interrupted");

	public Task AfterResponseWhileLockedAsync()
	{
		Add("afterResponse");
		return AfterResponse?.Invoke() ?? Task.CompletedTask;
	}

	public IReadOnlyList<string> Snapshot()
	{
		lock (_gate) return [.. Calls];
	}
}

/// <summary>Builders of the parsed stream events the fakes emit.</summary>
/// <remarks>Created by Claude</remarks>
internal static class Evt
{
	public static ClaudeStreamEvent Assistant(string text) =>
		new() { Type = "assistant", Content = text };

	public static ClaudeStreamEvent Result(string? sessionId = "sess-new", int inputTokens = 0, int outputTokens = 0, double cost = 0) =>
		new() { Type = "result", SessionId = sessionId, InputTokens = inputTokens, OutputTokens = outputTokens, CostUsd = cost };

	public static ClaudeStreamEvent ResultError(string text, string? sessionId = null) =>
		new() { Type = "result", IsError = true, Content = text, SessionId = sessionId };

	public static ClaudeStreamEvent SystemError(string text) =>
		new() { Type = "system", Subtype = "error", IsError = true, Content = text };

	public static ClaudeStreamEvent Compact() =>
		new() { Type = "system", Subtype = "compact" };

	public static ClaudeStreamEvent Progress(string text, string subtype = "task_progress") =>
		new() { Type = "system", Subtype = subtype, Content = text };
}

/// <summary>Awaiting helpers with a timeout, so a hung test fails instead of hanging the run.</summary>
/// <remarks>Created by Claude</remarks>
internal static class Waits
{
	public static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);

	public static Task<T> Within<T>(this Task<T> task) => task.WaitAsync(Limit);
	public static Task Within(this Task task) => task.WaitAsync(Limit);

	public static async Task Until(Func<bool> condition)
	{
		var deadline = DateTime.UtcNow + Limit;
		while (!condition())
		{
			if (DateTime.UtcNow > deadline)
				throw new TimeoutException("condition was not met in time");
			await Task.Delay(5);
		}
	}

	public static IReadOnlyList<string> Of(IEnumerable<string> log, string prefix) => [.. log.Where(l => l.StartsWith(prefix, StringComparison.Ordinal))];
}
