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
	public Func<Action<ClaudeStreamEvent>, Task>? EmitEvents { get; set; }

	public async Task SendMessageAsync(
		string workingDirectory, string claudePath, string? sessionId, string userMessage,
		Action<ClaudeStreamEvent> onEvent, string? model = null, string? profileConfigDir = null,
		string? effort = null, string? mcpConfigPath = null, string? ollamaBaseUrl = null,
		bool disableTools = false, bool chrome = false, bool disallowNativeScheduling = false,
		LocalToolSpec? localToolSpec = null, bool strictMcpConfig = false,
		CancellationToken cancellationToken = default)
	{
		Calls.Add((workingDirectory, sessionId, userMessage));
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
	public List<(string fileName, string role, string content)> AppendCalls { get; } = new();
	public IReadOnlyList<SessionEntryModel> ReadEntriesResult { get; set; } = Array.Empty<SessionEntryModel>();

	public event EventHandler<SessionEntryAppendedEventArgs>? EntryAppended { add { } remove { } }

	public string CreateSessionFile() => throw new NotImplementedException();

	public void AppendMessage(string fileName, string role, string content, string? profileName = null, string? modelId = null, string? effort = null)
		=> AppendCalls.Add((fileName, role, content));

	public void AppendCompactionSeparator(string fileName) => throw new NotImplementedException();
	public IReadOnlyList<SessionEntryModel> ReadEntries(string fileName) => ReadEntriesResult;
	public bool SessionFileExists(string fileName) => throw new NotImplementedException();
	public void DeleteSessionFile(string fileName) => throw new NotImplementedException();
	public string GetFullPath(string fileName) => throw new NotImplementedException();
	public void RewriteSessionFile(string fileName, string content) => throw new NotImplementedException();
	public void WriteSessionFile(string fileName, IReadOnlyList<SessionEntryModel> entries) => throw new NotImplementedException();
	public int RepairCorruptedCompactions() => throw new NotImplementedException();
}

internal sealed class FakeRegistryForTurnService : ISessionRegistry
{
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
	public SessionNodeModel? CreateSessionForAgent(string workingDir, string name, string? group, int orchestrationDepth = 0, string? supervisorNodeId = null) => throw new NotImplementedException();
	public List<SessionSummaryModel> BuildAgentSessionSummaries() => throw new NotImplementedException();
	public IReadOnlySet<string> CollectAllClaudeSessionIds() => throw new NotImplementedException();
	public (string directoryLabel, string treePath) GetSessionLocation(SessionNodeModel session) => throw new NotImplementedException();

	public void SetSessionRunning(string nodeId, bool isRunning) => SetRunningCalls.Add((nodeId, isRunning));
	public bool IsSessionRunning(string nodeId) => throw new NotImplementedException();
}

internal sealed class FakeProfileServiceForTurnService : IClaudeProfileService
{
	public string ProfilesRootDirectory => throw new NotImplementedException();
	public Task<string?> GetAccountEmailAsync(string claudePath, string? configDir) => throw new NotImplementedException();
	public Task LaunchAuthLoginAsync(string claudePath, string? configDir) => throw new NotImplementedException();
	public string? GetConfigDirForProfile(int profileIndex, IReadOnlyList<ClaudeProfileModel> profiles) => throw new NotImplementedException();
	public void MarkProfileDirectoryRemoved(string profileId) => throw new NotImplementedException();
}

internal sealed class FakeSessionStatusServiceForTurnService : IClaudeSessionStatusService
{
	public bool IsSessionResumable(string workingDirectory, string claudeSessionId) => throw new NotImplementedException();
	public bool TryAdoptSessionFromOtherProfile(string workingDirectory, string claudeSessionId, string? activeProfileConfigDir) => throw new NotImplementedException();
}

internal sealed class FakeModelServiceForTurnService : IClaudeModelService
{
	public IReadOnlyList<ClaudeModelInfo> GetCachedModels() => Array.Empty<ClaudeModelInfo>();
	public Task EnsureModelsLoadedAsync() => throw new NotImplementedException();
	public event EventHandler? ModelsUpdated { add { } remove { } }
}

internal sealed class FakeMcpServerForTurnService : IAgentMcpServer
{
	public int Port => 0;
	public void Start() => throw new NotImplementedException();
	public void Stop() => throw new NotImplementedException();
	public Task<string?> EnsureConfigFileAsync(string nodeId, string agentToken) => throw new NotImplementedException();
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
	private readonly FakeStreamingManagerForTurnService _streamingManager = new();
	private readonly FakeFileServiceForTurnService _fileService = new();
	private readonly FakeRegistryForTurnService _registry = new();
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
			new FakeProfileServiceForTurnService(),
			new Lazy<IAgentMcpServer>(() => new FakeMcpServerForTurnService()),
			_registry,
			new FakeSessionStatusServiceForTurnService(),
			new FakeModelServiceForTurnService(),
			new Lazy<ITelegramBridgeService>(() => new FakeBridgeForTurnService()),
			_streamingManager);
	}

	public void Dispose()
	{
		if (Directory.Exists(_tempDir))
			Directory.Delete(_tempDir, recursive: true);
	}

	private static SessionNodeModel MakeNode(string nodeId = "node-1") =>
		new() { NodeId = nodeId, Name = "Test", FileName = $"{nodeId}.txt", WorkingDirectory = @"C:\proj" };

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
}
