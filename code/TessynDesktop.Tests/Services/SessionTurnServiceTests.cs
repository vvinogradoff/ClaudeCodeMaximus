using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Models.Agent;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

// ── Fakes ─────────────────────────────────────────────────────────────────────

internal sealed class FakeProcessManagerForTurnService : IClaudeProcessManager
{
	public int ActiveProcessCount => 0;
	public List<(string workingDirectory, string? sessionId, string userMessage)> Calls { get; } = new();

	/// <summary>The model, effort and profile config dir of every spawn, parallel to <see cref="Calls"/>.</summary>
	public List<(string? model, string? effort, string? profileConfigDir)> Spawns { get; } = new();
	public Func<Action<ClaudeStreamEvent>, Task>? EmitEvents { get; set; }

	public async Task SendMessageAsync(
		string workingDirectory, string claudePath, string? sessionId, string userMessage,
		Action<ClaudeStreamEvent> onEvent, string? model = null, string? profileConfigDir = null,
		string? effort = null, string? mcpConfigPath = null, string? ollamaBaseUrl = null,
		bool disableTools = false, bool chrome = false, bool disallowNativeScheduling = false,
		LocalToolSpec? localToolSpec = null, bool strictMcpConfig = false,
		CancellationToken cancellationToken = default)
	{
		// Turns on different threads can reach this concurrently (queued-turn tests): List<T> is not thread-safe.
		lock (Calls)
		{
			Calls.Add((workingDirectory, sessionId, userMessage));
			Spawns.Add((model, effort, profileConfigDir));
		}
		if (EmitEvents != null)
			await EmitEvents(onEvent);
	}

	public Task<string?> RunPrintModeAsync(string claudePath, string prompt, string? model = null,
		string? profileConfigDir = null, int timeoutMs = 60000, CancellationToken cancellationToken = default) =>
		throw new NotImplementedException();

	public void TerminateAll() => throw new NotImplementedException();
}

/// <summary>
/// Fake for the process pool a Streaming Session Mode turn would be routed into (FR.19.13).
/// <see cref="SendTurnAsync"/>'s <c>onTurnEvent</c> callback mirrors the real manager's contract:
/// additive, does not touch any "primary" callback (there is none to simulate here — that is the
/// whole point of the fix under test, see <see cref="SessionTurnServiceTests"/>).
/// </summary>
internal sealed class FakeStreamingManagerForTurnService : IClaudeStreamingSessionManager
{
	public bool AliveResult { get; set; }
	public List<(string nodeId, string userMessage)> SendTurnCalls { get; } = new();
	public Func<Action<ClaudeStreamEvent>, Task>? EmitTurnEvents { get; set; }

	public bool EnsureStarted(string nodeId, StreamingSpawnParams spawnParams, Action<ClaudeStreamEvent> onEvent) =>
		throw new NotImplementedException();

	public async Task SendTurnAsync(string nodeId, string userMessage, CancellationToken cancellationToken, Action<ClaudeStreamEvent>? onTurnEvent = null)
	{
		SendTurnCalls.Add((nodeId, userMessage));
		if (EmitTurnEvents != null && onTurnEvent != null)
			await EmitTurnEvents(onTurnEvent);
	}

	public bool IsAlive(string nodeId) => AliveResult;
	public void Stop(string nodeId) { }
	public void TerminateAll() { }
	public event Action<string>? ProcessExited { add { } remove { } }
}

internal sealed class FakeFileServiceForTurnService : ISessionFileService
{
    public event EventHandler<SessionFileRewrittenEventArgs>? SessionFileRewritten { add { } remove { } }
    public event EventHandler<SessionFileDeletedEventArgs>? SessionFileDeleted { add { } remove { } }
    public IReadOnlyList<SessionEntryModel> ReadTailEntries(string fileName, int maxBytes) => Array.Empty<SessionEntryModel>();
    public IReadOnlyList<SessionEntryModel> ReadEntriesFrom(string fileName, long startOffset) => Array.Empty<SessionEntryModel>();
    public SessionFileStampModel? GetFileStamp(string fileName) => null;
	public List<(string fileName, string role, string content)> AppendCalls { get; } = new();

	/// <summary>Every appended message with its header attributes, parallel to <see cref="AppendCalls"/>.</summary>
	public List<(string role, string content, string? profileName, string? modelId, string? effort)> AppendDetails { get; } = new();
	public IReadOnlyList<SessionEntryModel> ReadEntriesResult { get; set; } = Array.Empty<SessionEntryModel>();

	public event EventHandler<SessionEntryAppendedEventArgs>? EntryAppended { add { } remove { } }
	public event EventHandler<SessionTurnCompletedEventArgs>? TurnCompleted { add { } remove { } }

	/// <summary>Every <see cref="NotifyTurnCompleted"/> call, with <see cref="AppendCalls"/>.Count at the time — so tests can check it came after the turn's last entry (FR.21.22).</summary>
	public List<(string fileName, bool cancelled, string? errorText, int appendCountAtCall)> TurnCompletedCalls { get; } = new();

	public void NotifyTurnCompleted(string fileName, bool cancelled, string? errorText)
		=> TurnCompletedCalls.Add((fileName, cancelled, errorText, AppendCalls.Count));

	public string CreateSessionFile() => throw new NotImplementedException();

	public void AppendMessage(string fileName, string role, string content, string? profileName = null, string? modelId = null, string? effort = null)
	{
		AppendCalls.Add((fileName, role, content));
		AppendDetails.Add((role, content, profileName, modelId, effort));
	}

	public void AppendCompactionSeparator(string fileName) => throw new NotImplementedException();
	public IReadOnlyList<SessionEntryModel> ReadEntries(string fileName) => ReadEntriesResult;
	public bool SessionFileExists(string fileName) => throw new NotImplementedException();
	public DateTimeOffset? GetLastWriteTimeUtc(string fileName) => throw new NotImplementedException();
	public void DeleteSessionFile(string fileName) => throw new NotImplementedException();
	public string GetFullPath(string fileName) => throw new NotImplementedException();
	public void RewriteSessionFile(string fileName, string content) => throw new NotImplementedException();
	public void WriteSessionFile(string fileName, IReadOnlyList<SessionEntryModel> entries) => throw new NotImplementedException();
	public int RepairCorruptedCompactions() => throw new NotImplementedException();
}

internal sealed class FakeRegistryForTurnService : ISessionRegistry
{
    public event EventHandler<SessionSettingsChangedEventArgs>? SessionSettingsChanged { add { } remove { } }
    public void RaiseSessionSettingsChanged(string nodeId) { }
    public DirectoryNodeModel? FindDirectoryByNodeId(string nodeId) => null;
    public GroupNodeModel? FindGroupByNodeId(string nodeId) => null;
	public List<(string nodeId, bool isRunning)> SetRunningCalls { get; } = new();

	public event EventHandler<TreeChangedEventArgs> TreeChanged { add { } remove { } }
	public IReadOnlyList<DirectoryNodeModel> Directories => throw new NotImplementedException();
	public string? SelectedNodeId { get; set; }

	public void LoadFromSettings() => throw new NotImplementedException();
	public SessionNodeModel? FindModelByNodeId(string nodeId) => throw new NotImplementedException();
	public SessionNodeModel? FindModelByAgentToken(string token) => throw new NotImplementedException();
	public SessionNodeModel? FindModelByFileName(string fileName) => throw new NotImplementedException();

	public DirectoryNodeModel AddDirectory(string path) => throw new NotImplementedException();
	public bool TryDeleteDirectory(DirectoryNodeModel directory) => throw new NotImplementedException();
	public GroupNodeModel AddGroup(DirectoryNodeModel parent, string name) => throw new NotImplementedException();
	public GroupNodeModel AddGroupToGroup(GroupNodeModel parent, string name) => throw new NotImplementedException();
	public bool TryDeleteGroup(DirectoryNodeModel parent, GroupNodeModel group) => throw new NotImplementedException();
	public bool TryDeleteGroupFromGroup(GroupNodeModel parent, GroupNodeModel group) => throw new NotImplementedException();
	public void RenameGroup(GroupNodeModel group, string newName) => throw new NotImplementedException();
	public SessionPlacementModel? CreateSessionAfter(string anchorNodeId, string name) => throw new NotImplementedException();
	public SessionNodeModel AddSession(DirectoryNodeModel parent, string name) => throw new NotImplementedException();
	public SessionNodeModel AddSessionToGroup(GroupNodeModel parent, string name) => throw new NotImplementedException();
	public SessionNodeModel ImportSession(DirectoryNodeModel parent, string name, string fileName, string claudeSessionId) => throw new NotImplementedException();
	public SessionNodeModel ImportSessionToGroup(GroupNodeModel parent, string name, string fileName, string claudeSessionId) => throw new NotImplementedException();
	public bool TryDeleteSession(DirectoryNodeModel parent, SessionNodeModel session, bool forceDelete = false) => throw new NotImplementedException();
	public bool TryDeleteSessionFromGroup(GroupNodeModel parent, SessionNodeModel session, bool forceDelete = false) => throw new NotImplementedException();
	public void RenameSession(SessionNodeModel session, string newName) => throw new NotImplementedException();
	public DirectoryNodeModel? RemoveSessionFromModel(SessionNodeModel session) => throw new NotImplementedException();
	public void InsertSessionIntoDirectory(DirectoryNodeModel parent, SessionNodeModel session, int index) => throw new NotImplementedException();
	public void InsertSessionIntoGroup(GroupNodeModel parent, SessionNodeModel session, int index) => throw new NotImplementedException();
	public bool MoveSession(SessionNodeModel session, DirectoryNodeModel targetDirectory, GroupNodeModel? targetGroup, int index) => throw new System.NotSupportedException();
	public void RaiseTreeModified() { }
	public void RaiseDaemonTreeMutation() { }
	public SessionNodeModel? CreateSessionForAgent(string workingDir, string name, string? group, int orchestrationDepth = 0, string? supervisorNodeId = null) => throw new NotImplementedException();
	public List<SessionSummaryModel> BuildAgentSessionSummaries() => throw new NotImplementedException();
	public IReadOnlyList<SessionSnapshotModel> SnapshotSessionsForListing() => throw new NotImplementedException();
	public List<SessionTagIndexSessionModel> SnapshotSessionsForTagIndex() => throw new NotImplementedException();
	public SessionListPageModel QueryAgentSessions(SessionListQueryModel query, string callerNodeId) => throw new NotImplementedException();
	public NodeIdResolutionModel ResolveNodeId(string idOrPrefix) => throw new NotImplementedException();
	public IReadOnlySet<string> CollectAllClaudeSessionIds() => throw new NotImplementedException();
	public (string directoryLabel, string treePath) GetSessionLocation(SessionNodeModel session) => throw new NotImplementedException();

	// A turn's trailing "not running" report and a queued turn's "running" report come from different
	// threads; List<T>.Add is not thread-safe, so serialise on the list (readers lock it too).
	public void SetSessionRunning(string nodeId, bool isRunning) { lock (SetRunningCalls) SetRunningCalls.Add((nodeId, isRunning)); }
	public bool IsSessionRunning(string nodeId) => throw new NotImplementedException();
}

internal sealed class FakeProfileServiceForTurnService : IClaudeProfileService
{
	public string ProfilesRootDirectory => @"C:\profiles";

	/// <summary>The account e-mail of the Default profile.</summary>
	public string? Email { get; set; } = "me@example.com";

	public Task<string?> GetAccountEmailAsync(string claudePath, string? configDir) => Task.FromResult(Email);
	public Task LaunchAuthLoginAsync(string claudePath, string? configDir) => throw new NotImplementedException();
	public string? GetConfigDirForProfile(int profileIndex, IReadOnlyList<ClaudeProfileModel> profiles) =>
		profileIndex >= 1 && profileIndex <= profiles.Count ? @"C:\profiles\" + profiles[profileIndex - 1].ProfileId : null;
	public void MarkProfileDirectoryRemoved(string profileId) => throw new NotImplementedException();
}

internal sealed class FakeSessionStatusServiceForTurnService : IClaudeSessionStatusService
{
	public bool IsSessionResumable(string workingDirectory, string claudeSessionId, IReadOnlyList<string>? projectsDirs = null) => throw new NotImplementedException();
	public IReadOnlyList<string> GetAllProjectsDirs() => throw new NotImplementedException();
	public IReadOnlyList<bool> AreSessionsResumable(IReadOnlyList<SessionResumabilityRequest> requests, IReadOnlyList<string>? projectsDirs = null) => throw new NotImplementedException();
	public bool TryAdoptSessionFromOtherProfile(string workingDirectory, string claudeSessionId, string? activeProfileConfigDir) => throw new NotImplementedException();
}

internal sealed class FakeModelServiceForTurnService : IClaudeModelService
{
	public IReadOnlyList<ClaudeModelInfo> Models { get; set; } = Array.Empty<ClaudeModelInfo>();
	public IReadOnlyList<ClaudeModelInfo> GetCachedModels() => Models;
	public Task EnsureModelsLoadedAsync() => throw new NotImplementedException();
	public event EventHandler? ModelsUpdated { add { } remove { } }
}

internal sealed class FakeMcpServerForTurnService : IAgentMcpServer
{
	public int Port => 0;
	public void Start() => throw new NotImplementedException();
	public void Stop() => throw new NotImplementedException();
	public Task<string?> EnsureConfigFileAsync(string nodeId, string agentToken) => Task.FromResult<string?>(null);
	public Task<string?> EnsureConfigFileAsync(string nodeId, string agentToken, bool includeCmx, IReadOnlyList<string> externalServerIds) => throw new NotImplementedException();
}

internal sealed class FakeBridgeForTurnService : ITelegramBridgeService
{
	public void Start() { }
	public void Stop() { }
	public Task EnsureTopicForSessionAsync(SessionNodeModel node) => Task.CompletedTask;
	public Task<bool> NotifyAsync(string nodeId, string message) => throw new NotImplementedException();
}

// ── Tests ──────────────────────────────────────────────────────────────────────

/// <remarks>Created by Claude</remarks>
public sealed class SessionTurnServiceTests : IDisposable
{
	private readonly string _tempDir;
	private readonly AppSettingsService _appSettings;
	private readonly FakeProcessManagerForTurnService _processManager = new();
	private readonly FakeProfileServiceForTurnService _profileService = new();
	private readonly FakeStreamingManagerForTurnService _streamingManager = new();
	private readonly FakeFileServiceForTurnService _fileService = new();
	private readonly FakeRegistryForTurnService _registry = new();
	private readonly AttentionTestEnv _attention = new();
	private readonly TagTestEnv _tagEnv = new();
	private readonly SessionTurnService _sut;

	public SessionTurnServiceTests()
	{
		_tempDir = Path.Combine(Path.GetTempPath(), $"turn-service-tests-{Guid.NewGuid():N}");
		Directory.CreateDirectory(_tempDir);
		_appSettings = new AppSettingsService(_tempDir);
		_appSettings.Settings.SessionFilesRoot = _tempDir;
		_appSettings.Settings.AgentToolsEnabled = false; // keeps IAgentMcpServer/EnsureConfigFileAsync unused

		_sut = new SessionTurnService(
			_processManager,
			_fileService,
			_appSettings,
			_profileService,
			new Lazy<IAgentMcpServer>(() => new FakeMcpServerForTurnService()),
			_registry,
			new FakeSessionStatusServiceForTurnService(),
			new FakeModelServiceForTurnService(),
			new Lazy<ITelegramBridgeService>(() => new FakeBridgeForTurnService()),
			_streamingManager,
			_attention.Service,
			_tagEnv.Service,
			new ProfileLabelService(_profileService, _appSettings));
	}

	public void Dispose()
	{
		_attention.Dispose();
		_tagEnv.Dispose();
		if (Directory.Exists(_tempDir))
			Directory.Delete(_tempDir, recursive: true);
	}

	private static SessionNodeModel MakeNode(string nodeId = "node-1") =>
		new() { NodeId = nodeId, Name = "Test", FileName = $"{nodeId}.txt", WorkingDirectory = @"C:\proj" };

	/// <summary>
	/// The Telegram bridge is a Lazy; resolving it used to sit outside the try that holds the finally, so a failing
	/// Lazy left the node marked running and the turn lock held forever.
	/// </summary>
	[Fact]
	public async Task RunTurnAsync_TelegramBridgeLazyThrows_ReleasesTurnLockAndClearsRunning()
	{
		var sut = new SessionTurnService(
			_processManager, _fileService, _appSettings, _profileService,
			new Lazy<IAgentMcpServer>(() => new FakeMcpServerForTurnService()),
			_registry, new FakeSessionStatusServiceForTurnService(), new FakeModelServiceForTurnService(),
			new Lazy<ITelegramBridgeService>(() => throw new InvalidOperationException("bridge ctor failed")),
			_streamingManager, _attention.Service, _tagEnv.Service,
			new ProfileLabelService(_profileService, _appSettings));
		var node = MakeNode();

		await Record.ExceptionAsync(() => sut.RunTurnAsync(node, "hello", TurnSource.Scheduled));

		Assert.Equal(1, sut.GetTurnLock(node.NodeId).CurrentCount);
		Assert.Equal((node.NodeId, false), _registry.SetRunningCalls[^1]);
	}

	// ── One-shot fallback (no streaming process alive) ───────────────────────────

	[Fact]
	public async Task RunTurnAsync_NoStreamingProcessAlive_UsesOneShotProcessManager()
	{
		_streamingManager.AliveResult = false;
		_processManager.EmitEvents = onEvent =>
		{
			onEvent(new ClaudeStreamEvent { Type = "assistant", Content = "hi there" });
			onEvent(new ClaudeStreamEvent { Type = "result", SessionId = "sess-1", InputTokens = 5, OutputTokens = 7, CostUsd = 0.01 });
			return Task.CompletedTask;
		};
		var node = MakeNode();

		var result = await _sut.RunTurnAsync(node, "hello", TurnSource.Scheduled);

		Assert.Single(_processManager.Calls);
		Assert.Empty(_streamingManager.SendTurnCalls);
		Assert.Equal("hi there", result.AssistantText);
		Assert.Equal("sess-1", node.ClaudeSessionId);
		Assert.Contains(_fileService.AppendCalls, c => c.role == Constants.SessionFile.RoleUser && c.content == "hello");
		Assert.Contains(_fileService.AppendCalls, c => c.role == Constants.SessionFile.RoleAssistant && c.content == "hi there");
	}

	// ── OAuth refresh-race auto-retry (ISSUE.010) ─────────────────────────────────

	[Fact]
	public async Task RunTurnAsync_OAuthRefreshRace_RetriesOnceAndSucceeds()
	{
		// A transient cross-process OAuth refresh-token race must not be reported as a real auth
		// failure — the turn should retry once, silently, and succeed.
		_streamingManager.AliveResult = false;
		var callCount = 0;
		_processManager.EmitEvents = onEvent =>
		{
			callCount++;
			if (callCount == 1)
			{
				onEvent(new ClaudeStreamEvent { Type = "result", IsError = true, Content = Constants.Auth.OAuthRefreshRaceMarker });
			}
			else
			{
				onEvent(new ClaudeStreamEvent { Type = "assistant", Content = "hi there" });
				onEvent(new ClaudeStreamEvent { Type = "result", SessionId = "sess-1" });
			}
			return Task.CompletedTask;
		};
		var node = MakeNode();

		var result = await _sut.RunTurnAsync(node, "hello", TurnSource.Scheduled);

		Assert.Equal(2, _processManager.Calls.Count);
		Assert.False(result.IsError);
		Assert.Equal("hi there", result.AssistantText);
		Assert.DoesNotContain(_fileService.AppendCalls, c => c.content.Contains(Constants.Auth.OAuthRefreshRaceMarker));
	}

	[Fact]
	public async Task RunTurnAsync_StreamingProcessAlive_OAuthRefreshRace_RetriesOnceAndSucceeds()
	{
		_streamingManager.AliveResult = true;
		var callCount = 0;
		_streamingManager.EmitTurnEvents = onTurnEvent =>
		{
			callCount++;
			if (callCount == 1)
			{
				onTurnEvent(new ClaudeStreamEvent { Type = "result", IsError = true, Content = Constants.Auth.OAuthRefreshRaceMarker });
			}
			else
			{
				onTurnEvent(new ClaudeStreamEvent { Type = "assistant", Content = "streamed reply" });
				onTurnEvent(new ClaudeStreamEvent { Type = "result" });
			}
			return Task.CompletedTask;
		};
		var node = MakeNode();

		var result = await _sut.RunTurnAsync(node, "hello", TurnSource.Scheduled);

		Assert.Equal(2, _streamingManager.SendTurnCalls.Count);
		Assert.False(result.IsError);
		Assert.Equal("streamed reply", result.AssistantText);
	}

	// ── Streaming redirect (FR.19.13 fix) ─────────────────────────────────────────

	[Fact]
	public async Task RunTurnAsync_StreamingProcessAlive_RoutesToStreamingManager_NotOneShot()
	{
		_streamingManager.AliveResult = true;
		_streamingManager.EmitTurnEvents = onTurnEvent =>
		{
			onTurnEvent(new ClaudeStreamEvent { Type = "assistant", Content = "streamed reply" });
			onTurnEvent(new ClaudeStreamEvent { Type = "result", InputTokens = 3, OutputTokens = 4, CostUsd = 0.02 });
			return Task.CompletedTask;
		};
		var node = MakeNode();
		node.ClaudeSessionId = "existing-sess";

		var result = await _sut.RunTurnAsync(node, "hello via telegram", TurnSource.Telegram);

		Assert.Empty(_processManager.Calls);
		Assert.Single(_streamingManager.SendTurnCalls);
		Assert.Equal(node.NodeId, _streamingManager.SendTurnCalls[0].nodeId);
		Assert.Equal("streamed reply", result.AssistantText);
		Assert.Equal(3, result.InputTokens);
		Assert.Equal(4, result.OutputTokens);
		Assert.Equal(0.02, result.CostUsd);
		Assert.False(result.IsError);
	}

	[Fact]
	public async Task RunTurnAsync_StreamingProcessAlive_DoesNotAppendAssistantContent_OnlyTheUserPrompt()
	{
		// This is the regression test for the actual bug: a competing one-shot --resume process
		// used to write both roles itself; now the already-bound primary streaming callback (owned
		// by the desktop TurnController that keeps the process alive) is solely responsible for the
		// assistant/system side — SessionTurnService must not also write it, or every headless turn
		// against a session with an open desktop tab would double-append.
		_streamingManager.AliveResult = true;
		_streamingManager.EmitTurnEvents = onTurnEvent =>
		{
			onTurnEvent(new ClaudeStreamEvent { Type = "assistant", Content = "streamed reply" });
			onTurnEvent(new ClaudeStreamEvent { Type = "result" });
			return Task.CompletedTask;
		};
		var node = MakeNode();

		await _sut.RunTurnAsync(node, "hello via telegram", TurnSource.Telegram);

		var call = Assert.Single(_fileService.AppendCalls);
		Assert.Equal(Constants.SessionFile.RoleUser, call.role);
		Assert.Equal("hello via telegram", call.content);
	}

	[Fact]
	public async Task RunTurnAsync_StreamingProcessAlive_ErrorResult_PropagatesToTurnResult()
	{
		_streamingManager.AliveResult = true;
		_streamingManager.EmitTurnEvents = onTurnEvent =>
		{
			onTurnEvent(new ClaudeStreamEvent { Type = "result", IsError = true, Content = "boom" });
			return Task.CompletedTask;
		};
		var node = MakeNode();

		var result = await _sut.RunTurnAsync(node, "hello", TurnSource.Scheduled);

		Assert.True(result.IsError);
		Assert.Equal("boom", result.ErrorMessage);
	}

	[Fact]
	public async Task RunTurnAsync_StreamingProcessAlive_SendsAugmentedPromptWithInstructionBlock()
	{
		_streamingManager.AliveResult = true;
		_streamingManager.EmitTurnEvents = onTurnEvent =>
		{
			onTurnEvent(new ClaudeStreamEvent { Type = "result" });
			return Task.CompletedTask;
		};
		var node = MakeNode();
		node.IsAutoCommit = true; // explicit commit to produce a non-empty instruction block

		await _sut.RunTurnAsync(node, "hello", TurnSource.Scheduled);

		var sentMessage = _streamingManager.SendTurnCalls[0].userMessage;
		Assert.StartsWith("hello", sentMessage);
		Assert.True(sentMessage.Length > "hello".Length, "Expected an instruction block appended to the raw prompt.");
	}

	[Fact]
	public async Task RunTurnAsync_StreamingProcessAlive_TopLevelSession_IncludesTelegramNotifyGuidance()
	{
		_appSettings.Settings.AgentToolsEnabled = true;
		_appSettings.Settings.TelegramEnabled = true;
		_streamingManager.AliveResult = true;
		_streamingManager.EmitTurnEvents = onTurnEvent =>
		{
			onTurnEvent(new ClaudeStreamEvent { Type = "result" });
			return Task.CompletedTask;
		};
		var node = MakeNode(); // OrchestrationDepth 0 — a top-level session, not a worker

		await _sut.RunTurnAsync(node, "hello", TurnSource.Scheduled);

		Assert.Contains(Constants.Instructions.TelegramNotifyGuidance, _streamingManager.SendTurnCalls[0].userMessage);
	}

	[Fact]
	public async Task RunTurnAsync_StreamingProcessAlive_WorkerSession_OmitsTelegramNotifyGuidance()
	{
		// FR.21.20 — a Worker Session's own notify_user call is dropped outright by the bridge
		// (FR.21.19), so it must never even be told the tool exists.
		_appSettings.Settings.AgentToolsEnabled = true;
		_appSettings.Settings.TelegramEnabled = true;
		_streamingManager.AliveResult = true;
		_streamingManager.EmitTurnEvents = onTurnEvent =>
		{
			onTurnEvent(new ClaudeStreamEvent { Type = "result" });
			return Task.CompletedTask;
		};
		var node = MakeNode();
		node.OrchestrationDepth = 1;

		await _sut.RunTurnAsync(node, "hello", TurnSource.Scheduled);

		Assert.DoesNotContain(Constants.Instructions.TelegramNotifyGuidance, _streamingManager.SendTurnCalls[0].userMessage);
	}

	[Fact]
	public async Task RunTurnAsync_StreamingProcessAlive_SetsRunningTrueThenFalse()
	{
		_streamingManager.AliveResult = true;
		_streamingManager.EmitTurnEvents = onTurnEvent =>
		{
			onTurnEvent(new ClaudeStreamEvent { Type = "result" });
			return Task.CompletedTask;
		};
		var node = MakeNode();

		await _sut.RunTurnAsync(node, "hello", TurnSource.Scheduled);

		Assert.Equal(new[] { (node.NodeId, true), (node.NodeId, false) }, _registry.SetRunningCalls);
	}

	// ── Turn Completed event (FR.21.22) ───────────────────────────────────────────

	[Fact]
	public async Task RunTurnAsync_RaisesTurnCompletedOnceAfterLastEntry()
	{
		_streamingManager.AliveResult = false;
		_processManager.EmitEvents = onEvent =>
		{
			onEvent(new ClaudeStreamEvent { Type = "assistant", Content = "hi there" });
			onEvent(new ClaudeStreamEvent { Type = "result", SessionId = "sess-1" });
			return Task.CompletedTask;
		};
		var node = MakeNode();

		await _sut.RunTurnAsync(node, "hello", TurnSource.Scheduled);

		var call = Assert.Single(_fileService.TurnCompletedCalls);
		Assert.Equal(node.FileName, call.fileName);
		Assert.False(call.cancelled);
		Assert.Null(call.errorText);
		Assert.Equal(_fileService.AppendCalls.Count, call.appendCountAtCall);
	}

	[Fact]
	public async Task RunTurnAsync_Cancelled_RaisesTurnCompletedWithCancelledTrue()
	{
		_streamingManager.AliveResult = false;
		_processManager.EmitEvents = _ => throw new OperationCanceledException();

		await _sut.RunTurnAsync(MakeNode(), "hello", TurnSource.Scheduled);

		var call = Assert.Single(_fileService.TurnCompletedCalls);
		Assert.True(call.cancelled);
		Assert.Null(call.errorText);
		Assert.Contains(_fileService.AppendCalls, c => c.content == "[Turn cancelled]");
		Assert.Equal(_fileService.AppendCalls.Count, call.appendCountAtCall);
	}

	[Fact]
	public async Task RunTurnAsync_ErrorResult_RaisesTurnCompletedWithErrorText()
	{
		const string limitText = "You've hit your session limit · resets 1:50pm";
		_streamingManager.AliveResult = false;
		_processManager.EmitEvents = onEvent =>
		{
			onEvent(new ClaudeStreamEvent { Type = "result", IsError = true, Content = limitText });
			return Task.CompletedTask;
		};

		await _sut.RunTurnAsync(MakeNode(), "hello", TurnSource.Scheduled);

		var call = Assert.Single(_fileService.TurnCompletedCalls);
		Assert.False(call.cancelled);
		Assert.Equal(limitText, call.errorText);
		Assert.Equal(_fileService.AppendCalls.Count, call.appendCountAtCall);
	}

	[Fact]
	public async Task RunTurnAsync_AuthRaceRetriedSuccessfully_NoErrorText()
	{
		_streamingManager.AliveResult = false;
		var callCount = 0;
		_processManager.EmitEvents = onEvent =>
		{
			callCount++;
			if (callCount == 1)
				onEvent(new ClaudeStreamEvent { Type = "result", IsError = true, Content = Constants.Auth.OAuthRefreshRaceMarker });
			else
			{
				onEvent(new ClaudeStreamEvent { Type = "assistant", Content = "hi there" });
				onEvent(new ClaudeStreamEvent { Type = "result", SessionId = "sess-1" });
			}
			return Task.CompletedTask;
		};

		await _sut.RunTurnAsync(MakeNode(), "hello", TurnSource.Scheduled);

		var call = Assert.Single(_fileService.TurnCompletedCalls);
		Assert.False(call.cancelled);
		Assert.Null(call.errorText);
	}

	[Fact]
	public async Task RunTurnAsync_UnexpectedException_StillRaisesTurnCompletedOnce()
	{
		_streamingManager.AliveResult = false;
		_processManager.EmitEvents = _ => throw new InvalidOperationException("kaput");

		await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.RunTurnAsync(MakeNode(), "hello", TurnSource.Scheduled));

		var call = Assert.Single(_fileService.TurnCompletedCalls);
		Assert.False(call.cancelled);
		Assert.Equal("kaput", call.errorText);
	}

	[Fact]
	public async Task RunTurnAsync_StreamingRedirect_RaisesTurnCompleted()
	{
		_streamingManager.AliveResult = true;
		_streamingManager.EmitTurnEvents = onTurnEvent =>
		{
			onTurnEvent(new ClaudeStreamEvent { Type = "assistant", Content = "streamed reply" });
			onTurnEvent(new ClaudeStreamEvent { Type = "result", IsError = true, Content = "boom" });
			return Task.CompletedTask;
		};

		await _sut.RunTurnAsync(MakeNode(), "hello", TurnSource.Scheduled);

		var call = Assert.Single(_fileService.TurnCompletedCalls);
		Assert.False(call.cancelled);
		Assert.Equal("boom", call.errorText);
	}

	// ── Scheduled-turn notify reminder (FR.21.24) ─────────────────────────────────

	[Theory]
	[InlineData(TurnSource.Scheduled, true)]
	[InlineData(TurnSource.User, false)]
	[InlineData(TurnSource.Orchestrated, false)]
	[InlineData(TurnSource.Telegram, false)]
	public async Task RunTurnAsync_ScheduledSource_SendsReminder_OtherSourcesDoNot(TurnSource source, bool expectReminder)
	{
		_appSettings.Settings.AgentToolsEnabled = true;
		_appSettings.Settings.TelegramEnabled = true;

		// One-shot call site.
		_streamingManager.AliveResult = false;
		_processManager.EmitEvents = onEvent =>
		{
			onEvent(new ClaudeStreamEvent { Type = "result", SessionId = "sess-1" });
			return Task.CompletedTask;
		};
		await _sut.RunTurnAsync(MakeNode("one-shot"), "hello", source);
		Assert.Equal(expectReminder, _processManager.Calls[0].userMessage.Contains(Constants.Instructions.ScheduledTurnNotifyReminder));

		// Streaming-redirect call site.
		_streamingManager.AliveResult = true;
		_streamingManager.EmitTurnEvents = onTurnEvent =>
		{
			onTurnEvent(new ClaudeStreamEvent { Type = "result" });
			return Task.CompletedTask;
		};
		await _sut.RunTurnAsync(MakeNode("streaming"), "hello", source);
		Assert.Equal(expectReminder, _streamingManager.SendTurnCalls[0].userMessage.Contains(Constants.Instructions.ScheduledTurnNotifyReminder));
	}

	// ── FR.23.11 — pinned list and take-once closures at both SessionTurnService call sites ──────

	private void PostPinnedAndClosedItems(SessionNodeModel node)
	{
		_attention.Service.Post(node, [AttentionTestEnv.Req("Which plan?", AttentionKind.Ask), AttentionTestEnv.Req("Rotate key", AttentionKind.Todo)]);
		_attention.Service.CloseByUser(node, "A2");
	}

	[Fact]
	public async Task RunTurnAsync_StreamingProcessAlive_ReportsPinnedItemsEveryTurnAndEachClosureOnce()   // TC.FV.15-10
	{
		_appSettings.Settings.AgentToolsEnabled = true;
		_streamingManager.AliveResult = true;
		_streamingManager.EmitTurnEvents = onTurnEvent =>
		{
			onTurnEvent(new ClaudeStreamEvent { Type = "result" });
			return Task.CompletedTask;
		};
		var node = MakeNode();
		PostPinnedAndClosedItems(node);

		await _sut.RunTurnAsync(node, "first", TurnSource.Scheduled);
		await _sut.RunTurnAsync(node, "second", TurnSource.Scheduled);

		var first  = _streamingManager.SendTurnCalls[0].userMessage;
		var second = _streamingManager.SendTurnCalls[1].userMessage;
		Assert.Contains("  - A1 ask: Which plan?", first);
		Assert.Contains("Closed by the user in the Focus view since your last turn: A2 todo \"Rotate key\" dismissed without an answer.", first);
		Assert.Contains("  - A1 ask: Which plan?", second);
		Assert.DoesNotContain("Closed by the user", second);
	}

	[Fact]
	public async Task RunTurnAsync_OneShot_ReportsPinnedItemsAndEachClosureOnce()   // TC.FV.15-10
	{
		_appSettings.Settings.AgentToolsEnabled = true;
		_streamingManager.AliveResult = false;
		_processManager.EmitEvents = onEvent =>
		{
			onEvent(new ClaudeStreamEvent { Type = "result", SessionId = "sess-1" });
			return Task.CompletedTask;
		};
		var node = MakeNode();
		PostPinnedAndClosedItems(node);

		await _sut.RunTurnAsync(node, "first", TurnSource.Scheduled);

		Assert.Contains("  - A1 ask: Which plan?", _processManager.Calls[0].userMessage);
		Assert.Contains("A2 todo \"Rotate key\" dismissed without an answer.", _processManager.Calls[0].userMessage);
		Assert.Empty(_attention.Service.TakeUnreportedUserClosures(node.NodeId));   // consumed by the turn
	}

	[Fact]
	public async Task RunTurnAsync_WorkerSession_NeverTakesTheClosures()   // FR.23.11
	{
		_appSettings.Settings.AgentToolsEnabled = true;
		_streamingManager.AliveResult = true;
		_streamingManager.EmitTurnEvents = onTurnEvent =>
		{
			onTurnEvent(new ClaudeStreamEvent { Type = "result" });
			return Task.CompletedTask;
		};
		var node = MakeNode();
		PostPinnedAndClosedItems(node);
		node.OrchestrationDepth = 1;

		await _sut.RunTurnAsync(node, "hello", TurnSource.Scheduled);

		Assert.DoesNotContain("Pinned in the user's Focus view", _streamingManager.SendTurnCalls[0].userMessage);
		node.OrchestrationDepth = 0;
		Assert.Single(_attention.Service.TakeUnreportedUserClosures(node.NodeId));   // still unreported
	}

	// ── FR.24.6 — tag guidance at both SessionTurnService call sites ──────────────────────

	[Fact]
	public async Task RunTurnAsync_StreamingProcessAlive_TagGuidanceOnTheFirstTurnOfTheRunOnly()   // TC.TG.15 / TC.TG.14
	{
		_appSettings.Settings.AgentToolsEnabled = true;
		_streamingManager.AliveResult = true;
		_streamingManager.EmitTurnEvents = onTurnEvent =>
		{
			onTurnEvent(new ClaudeStreamEvent { Type = "result" });
			return Task.CompletedTask;
		};
		var node = MakeNode();
		node.ClaudeSessionId = "existing";   // a long-running session, not a fresh one
		_tagEnv.Service.Edit(node, ["o/r#7"], []);

		await _sut.RunTurnAsync(node, "first", TurnSource.Scheduled);
		await _sut.RunTurnAsync(node, "second", TurnSource.Scheduled);

		Assert.Contains(Constants.Instructions.SessionTagGuidance + " Current tags: r PR #7.", _streamingManager.SendTurnCalls[0].userMessage);
		Assert.DoesNotContain("tag_session", _streamingManager.SendTurnCalls[1].userMessage);
	}

	[Fact]
	public async Task RunTurnAsync_OneShot_TagGuidanceOnTheFirstTurn()   // TC.TG.14
	{
		_appSettings.Settings.AgentToolsEnabled = true;
		_streamingManager.AliveResult = false;
		_processManager.EmitEvents = onEvent =>
		{
			onEvent(new ClaudeStreamEvent { Type = "result", SessionId = "sess-1" });
			return Task.CompletedTask;
		};

		await _sut.RunTurnAsync(MakeNode(), "first", TurnSource.Scheduled);

		Assert.Contains(Constants.Instructions.SessionTagGuidance, _processManager.Calls[0].userMessage);
		Assert.DoesNotContain("Current tags:", _processManager.Calls[0].userMessage);
	}

	[Theory]
	[InlineData(false, false)]   // agent tools off
	[InlineData(true, true)]     // worker session
	public async Task RunTurnAsync_WhenTheGuidanceCannotBeShown_ItStaysDue(bool agentTools, bool worker)   // TC.TG.14-5
	{
		_appSettings.Settings.AgentToolsEnabled = agentTools;
		_streamingManager.AliveResult = true;
		_streamingManager.EmitTurnEvents = onTurnEvent =>
		{
			onTurnEvent(new ClaudeStreamEvent { Type = "result" });
			return Task.CompletedTask;
		};
		var node = MakeNode();
		node.OrchestrationDepth = worker ? 1 : 0;

		await _sut.RunTurnAsync(node, "hello", TurnSource.Scheduled);

		Assert.DoesNotContain("tag_session", _streamingManager.SendTurnCalls[0].userMessage);
		Assert.True(_tagEnv.Service.TakeTagGuidanceDue(node.NodeId));   // not consumed by the turn
	}

	// ── Per-turn options (TurnSource.Web) ─────────────────────────────────────────

	private void EmitOneAnswer()
	{
		_streamingManager.AliveResult = false;
		_processManager.EmitEvents = onEvent =>
		{
			onEvent(new ClaudeStreamEvent { Type = "assistant", Content = "answer" });
			onEvent(new ClaudeStreamEvent { Type = "assistant", Content = "second part" });
			onEvent(new ClaudeStreamEvent { Type = "result", SessionId = "sess-1" });
			return Task.CompletedTask;
		};
	}

	private (string role, string content, string? profileName, string? modelId, string? effort) FirstAssistant() =>
		_fileService.AppendDetails.First(d => d.role == Constants.SessionFile.RoleAssistant);

	[Fact]
	public async Task RunTurnAsync_WithoutOptions_PassesNoEffortAndStampsDefault()
	{
		EmitOneAnswer();

		await _sut.RunTurnAsync(MakeNode(), "hello", TurnSource.Telegram);

		Assert.Null(_processManager.Spawns[0].effort);
		Assert.Null(_processManager.Spawns[0].model);
		Assert.Equal(("default", "default"), (FirstAssistant().modelId, FirstAssistant().effort));
	}

	[Fact]
	public async Task RunTurnAsync_OptionsNull_BehavesLikeTheOldOverload()
	{
		EmitOneAnswer();
		var node = MakeNode();
		node.ModelId = "claude-sonnet-5-5";

		await _sut.RunTurnAsync(node, "hello", TurnSource.Web, turnOptions: null);

		Assert.Equal("claude-sonnet-5-5", _processManager.Spawns[0].model);
		Assert.Null(_processManager.Spawns[0].effort);
	}

	[Fact]
	public async Task RunTurnAsync_ModelOption_IsPassedAndStampedOnTheFirstAssistantEntryOnly()
	{
		EmitOneAnswer();
		var node = MakeNode();
		node.ModelId = "claude-sonnet-5-5";

		await _sut.RunTurnAsync(node, "hello", TurnSource.Web, new TurnOptionsModel(ModelId: "claude-opus-5-5"));

		Assert.Equal("claude-opus-5-5", _processManager.Spawns[0].model);
		var assistants = _fileService.AppendDetails.Where(d => d.role == Constants.SessionFile.RoleAssistant).ToList();
		Assert.Equal("claude-opus-5-5", assistants[0].modelId);
		Assert.Null(assistants[1].modelId);
		Assert.Null(assistants[1].effort);
	}

	[Fact]
	public async Task RunTurnAsync_DefaultModelOption_MeansNoModelFlagEvenWhenTheNodeHasOne()
	{
		EmitOneAnswer();
		var node = MakeNode();
		node.ModelId = "claude-sonnet-5-5";

		await _sut.RunTurnAsync(node, "hello", TurnSource.Web, new TurnOptionsModel(ModelId: "default"));

		Assert.Null(_processManager.Spawns[0].model);
		Assert.Equal("default", FirstAssistant().modelId);
	}

	[Fact]
	public async Task RunTurnAsync_EffortOption_IsPassedAsTheEffortFlagAndStamped()
	{
		EmitOneAnswer();

		await _sut.RunTurnAsync(MakeNode(), "hello", TurnSource.Web, new TurnOptionsModel(Effort: "high"));

		Assert.Equal("high", _processManager.Spawns[0].effort);
		Assert.Equal("high", FirstAssistant().effort);
	}

	[Theory]
	[InlineData("default")]
	[InlineData("")]
	public async Task RunTurnAsync_DefaultEffortOption_PassesNoFlagAndStampsDefault(string effort)
	{
		EmitOneAnswer();

		await _sut.RunTurnAsync(MakeNode(), "hello", TurnSource.Web, new TurnOptionsModel(Effort: effort));

		Assert.Null(_processManager.Spawns[0].effort);
		Assert.Equal("default", FirstAssistant().effort);
	}

	// ── Profile stamping on the USER entry ────────────────────────────────────────

	private (string role, string content, string? profileName, string? modelId, string? effort) UserEntry() =>
		_fileService.AppendDetails.First(d => d.role == Constants.SessionFile.RoleUser);

	[Theory]
	[InlineData(TurnSource.User)]
	[InlineData(TurnSource.Scheduled)]
	[InlineData(TurnSource.Orchestrated)]
	[InlineData(TurnSource.Telegram)]
	[InlineData(TurnSource.Web)]
	public async Task RunTurnAsync_NoProfileRecorded_StampsTheDefaultLabelOnTheUserEntry(TurnSource source)
	{
		EmitOneAnswer();

		await _sut.RunTurnAsync(MakeNode(), "hello", source);

		Assert.Equal("[Sys] me@example.com", UserEntry().profileName);
		Assert.Null(_processManager.Spawns[0].profileConfigDir);
	}

	[Fact]
	public async Task RunTurnAsync_DefaultLabelFallsBackWhenTheEmailIsUnknown()
	{
		EmitOneAnswer();
		_profileService.Email = null;

		await _sut.RunTurnAsync(MakeNode(), "hello", TurnSource.User);

		Assert.Equal("[Sys] Default", UserEntry().profileName);
	}

	[Fact]
	public async Task RunTurnAsync_PinnedStoredProfile_IsStampedWithItsDisplayNameAndUsed()
	{
		EmitOneAnswer();
		_appSettings.Settings.Profiles.Add(new ClaudeProfileModel { ProfileId = "p1", DisplayName = "Work" });
		var node = MakeNode();
		node.ProfileName = "work";   // matched case-insensitively

		await _sut.RunTurnAsync(node, "hello", TurnSource.Scheduled);

		Assert.Equal("Work", UserEntry().profileName);
		Assert.Equal(@"C:\profiles\p1", _processManager.Spawns[0].profileConfigDir);
	}

	[Fact]
	public async Task RunTurnAsync_ProfileOption_OverridesThePinnedProfile()
	{
		EmitOneAnswer();
		_appSettings.Settings.Profiles.Add(new ClaudeProfileModel { ProfileId = "p1", DisplayName = "Work" });
		_appSettings.Settings.Profiles.Add(new ClaudeProfileModel { ProfileId = "p2", DisplayName = "Home" });
		var node = MakeNode();
		node.ProfileName = "Work";

		await _sut.RunTurnAsync(node, "hello", TurnSource.Web, new TurnOptionsModel(ProfileName: "Home"));

		Assert.Equal("Home", UserEntry().profileName);
		Assert.Equal(@"C:\profiles\p2", _processManager.Spawns[0].profileConfigDir);
	}

	[Fact]
	public async Task RunTurnAsync_DefaultLabelOption_UsesTheDefaultProfileEvenWhenOneIsPinned()
	{
		EmitOneAnswer();
		_appSettings.Settings.Profiles.Add(new ClaudeProfileModel { ProfileId = "p1", DisplayName = "Work" });
		var node = MakeNode();
		node.ProfileName = "Work";

		await _sut.RunTurnAsync(node, "hello", TurnSource.Web, new TurnOptionsModel(ProfileName: "[Sys] me@example.com"));

		Assert.Equal("[Sys] me@example.com", UserEntry().profileName);
		Assert.Null(_processManager.Spawns[0].profileConfigDir);
	}

	/// <summary>ISSUE.013 R2: a pinned profile that no longer resolves is an error naming it, never a silent fallback to Default.</summary>
	[Fact]
	public async Task RunTurnAsync_IdPinnedProfileThatNoLongerExists_IsARefusedTurnAndNeverRunsOnDefault()
	{
		EmitOneAnswer();
		var node = MakeNode();
		node.ProfileId   = "p9";
		node.ProfileName = "Gone";

		var result = await _sut.RunTurnAsync(node, "hello", TurnSource.Scheduled);

		Assert.True(result.IsError);
		Assert.True(result.Refused);
		Assert.Contains("'Gone'", result.ErrorMessage);
		Assert.Empty(_processManager.Calls);
		Assert.Empty(_fileService.AppendDetails);
	}

	/// <summary>Supervisor decision (P2): a legacy name-only record that matches nothing runs on Default as before, with a note on the result.</summary>
	[Fact]
	public async Task RunTurnAsync_LegacyNameThatMatchesNoProfile_RunsOnDefault_AndTheResultStatesIt()
	{
		EmitOneAnswer();
		var node = MakeNode();
		node.ProfileName = "Gone";

		var result = await _sut.RunTurnAsync(node, "hello", TurnSource.Scheduled);

		Assert.False(result.IsError);
		Assert.False(result.Refused);
		Assert.Equal("the recorded profile 'Gone' no longer exists", result.Note);
		Assert.Null(_processManager.Spawns[0].profileConfigDir);
		Assert.Equal("[Sys] me@example.com", UserEntry().profileName);
	}

	[Fact]
	public async Task RunTurnAsync_HistoryDerivedNameThatMatchesNoProfile_RunsOnDefault_AndTheResultStatesIt()
	{
		EmitOneAnswer();
		_fileService.ReadEntriesResult =
		[
			new SessionEntryModel { Timestamp = DateTimeOffset.UtcNow, Role = Constants.SessionFile.RoleUser, Content = "old", ProfileName = "Retired account" },
		];

		var result = await _sut.RunTurnAsync(MakeNode(), "hello", TurnSource.Telegram);

		Assert.False(result.IsError);
		Assert.Contains("'Retired account'", result.Note);
		Assert.Null(_processManager.Spawns[0].profileConfigDir);
	}

	[Fact]
	public async Task RunTurnAsync_ProfileFromTheLastUserEntry_IsStillHonoured()
	{
		EmitOneAnswer();
		_appSettings.Settings.Profiles.Add(new ClaudeProfileModel { ProfileId = "p1", DisplayName = "Work" });
		_fileService.ReadEntriesResult =
		[
			new SessionEntryModel { Timestamp = DateTimeOffset.UtcNow, Role = Constants.SessionFile.RoleUser, Content = "old", ProfileName = "Work" },
		];

		await _sut.RunTurnAsync(MakeNode(), "hello", TurnSource.Telegram);

		Assert.Equal("Work", UserEntry().profileName);
		Assert.Equal(@"C:\profiles\p1", _processManager.Spawns[0].profileConfigDir);
	}

	[Fact]
	public async Task RunTurnAsync_StreamingRedirect_IgnoresOptionsButStillStampsTheUserEntry()
	{
		_streamingManager.AliveResult = true;
		_streamingManager.EmitTurnEvents = onTurnEvent =>
		{
			onTurnEvent(new ClaudeStreamEvent { Type = "result" });
			return Task.CompletedTask;
		};

		await _sut.RunTurnAsync(MakeNode(), "hello", TurnSource.Web, new TurnOptionsModel("claude-opus-5-5", "high", "Home"));

		Assert.Empty(_processManager.Calls);
		Assert.Single(_streamingManager.SendTurnCalls);
		Assert.Equal("[Sys] me@example.com", UserEntry().profileName);
	}

	// ── Running flag across queued turns (robustness item 0.6/2) ──────────────────

	/// <summary>
	/// A turn queued behind another on the same node must leave the node running: the first turn's
	/// "not running" report (which comes after its lock release) must not cancel out the queued turn's
	/// "running" report. SessionRegistry reference-counts the reports (FR.1.8), so the calls must balance.
	/// </summary>
	[Fact]
	public async Task RunTurnAsync_QueuedTurn_NodeStaysRunningUntilLastTurnEnds()
	{
		_streamingManager.AliveResult = false;
		var gate = new SemaphoreSlim(0);
		_processManager.EmitEvents = async _ => await gate.WaitAsync();
		var node = MakeNode();

		int RunningCount()
		{
			var count = 0;
			(string nodeId, bool isRunning)[] reports;
			lock (_registry.SetRunningCalls) reports = _registry.SetRunningCalls.ToArray();
			foreach (var (_, running) in reports)
				count = running ? count + 1 : Math.Max(0, count - 1);
			return count;
		}

		var first = _sut.RunTurnAsync(node, "one", TurnSource.Scheduled);
		await WaitUntilAsync(() => { lock (_processManager.Calls) return _processManager.Calls.Count == 1; });
		var second = _sut.RunTurnAsync(node, "two", TurnSource.Scheduled); // queued on the turn lock

		gate.Release();
		await first;
		await WaitUntilAsync(() => { lock (_processManager.Calls) return _processManager.Calls.Count == 2; });
		Assert.True(RunningCount() > 0, "node reported not running while the queued turn is executing");

		gate.Release();
		await second;
		Assert.Equal(0, RunningCount());
	}

	private static async Task WaitUntilAsync(Func<bool> condition)
	{
		var deadline = DateTime.UtcNow.AddSeconds(10);
		while (!condition())
		{
			Assert.True(DateTime.UtcNow < deadline, "condition not reached in time");
			await Task.Delay(5);
		}
	}
}
