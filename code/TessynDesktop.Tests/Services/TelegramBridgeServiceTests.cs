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

internal sealed class FakeTelegramClientForBridge : ITelegramClient
{
	public List<(long chatId, string text, int? threadId, bool disableNotification)> SentMessages { get; } = new();
	public List<(long chatId, string name)> CreatedTopics { get; } = new();
	public Queue<IReadOnlyList<TelegramUpdate>> UpdateBatches { get; } = new();
	public int NextThreadId { get; set; } = 100;

	public Task SendMessageAsync(long chatId, string text, int? messageThreadId, bool disableNotification, CancellationToken ct = default)
	{
		lock (SentMessages) SentMessages.Add((chatId, text, messageThreadId, disableNotification));
		return Task.CompletedTask;
	}

	public Task<int> CreateForumTopicAsync(long chatId, string name, CancellationToken ct = default)
	{
		lock (CreatedTopics) CreatedTopics.Add((chatId, name));
		return Task.FromResult(NextThreadId++);
	}

	public List<(long chatId, int threadId, string name)> EditedTopics { get; } = new();

	/// <summary>Per-call behaviour of <see cref="EditForumTopicAsync"/>; default = rename succeeds (true).</summary>
	public Func<int, string, CancellationToken, Task<bool>>? EditBehavior { get; set; }

	public Task<bool> EditForumTopicAsync(long chatId, int messageThreadId, string name, CancellationToken ct = default)
	{
		lock (EditedTopics) EditedTopics.Add((chatId, messageThreadId, name));
		return EditBehavior?.Invoke(messageThreadId, name, ct) ?? Task.FromResult(true);
	}

	public List<(long chatId, int threadId)> DeletedTopics { get; } = new();

	/// <summary>Per-call behaviour of <see cref="DeleteForumTopicAsync"/>; default = delete succeeds (true).</summary>
	public Func<int, CancellationToken, Task<bool>>? DeleteBehavior { get; set; }

	public Task<bool> DeleteForumTopicAsync(long chatId, int messageThreadId, CancellationToken ct = default)
	{
		lock (DeletedTopics) DeletedTopics.Add((chatId, messageThreadId));
		return DeleteBehavior?.Invoke(messageThreadId, ct) ?? Task.FromResult(true);
	}

	public List<(long offset, int timeoutSeconds)> Polls { get; } = new();
	public Dictionary<string, TelegramFileInfo> Files { get; } = new();
	public Dictionary<string, Exception> GetFileErrors { get; } = new();
	public Exception? DownloadError { get; set; }
	public List<(string filePath, string destination)> Downloads { get; } = new();

	public async Task<IReadOnlyList<TelegramUpdate>> GetUpdatesAsync(long offset, int timeoutSeconds, CancellationToken ct = default)
	{
		IReadOnlyList<TelegramUpdate>? batch = null;
		lock (UpdateBatches)
		{
			Polls.Add((offset, timeoutSeconds));
			if (UpdateBatches.Count > 0)
				batch = UpdateBatches.Dequeue();
		}

		if (batch != null)
			return batch;

		// Short follow-up polls (album handling, FR.21.16) just come back empty.
		if (timeoutSeconds < Constants.Telegram.LongPollTimeoutSeconds)
		{
			await Task.Delay(20, ct);
			return Array.Empty<TelegramUpdate>();
		}

		// No more canned batches — block like a real long-poll with nothing new, until cancelled.
		await Task.Delay(Timeout.Infinite, ct);
		return Array.Empty<TelegramUpdate>();
	}

	public Task<TelegramFileInfo> GetFileAsync(string fileId, CancellationToken ct = default)
	{
		if (GetFileErrors.TryGetValue(fileId, out var error))
			throw error;
		return Task.FromResult(Files.TryGetValue(fileId, out var info) ? info : new TelegramFileInfo($"files/{fileId}", 10));
	}

	public Task DownloadFileAsync(string filePath, string destinationPath, CancellationToken ct = default)
	{
		if (DownloadError != null)
			throw DownloadError;
		lock (Downloads) Downloads.Add((filePath, destinationPath));
		File.WriteAllText(destinationPath, "data");
		return Task.CompletedTask;
	}
}

internal sealed class FakeDirectoryLabelServiceForTelegram : IDirectoryLabelService
{
	public string GetLabel(string directoryPath) => "TestDir";
}

/// <summary>
/// Fake <see cref="IDaemonClient"/> scoped to what <see cref="TelegramBridgeService"/> actually
/// calls (unused members throw, matching the convention already used by <c>InProcFakeFileService</c>
/// etc. in InProcDaemonClientTests.cs). <see cref="RunTurnAsync"/> simulates
/// <c>SessionTurnService.RunTurnAsync</c>'s real behavior of appending a ROLE=USER entry (the
/// prompt) before running, then an optional ROLE=ASSISTANT reply — both raised via
/// <see cref="EntryAppended"/> — so tests can exercise the mirror and echo suppression end-to-end
/// without a real SessionTurnService.
/// </summary>
internal sealed class FakeDaemonClientForTelegram : IDaemonClient
{
    public event EventHandler<SessionSettingsChangedEventArgs>? SessionSettingsChanged { add { } remove { } }
	private readonly Dictionary<string, SessionNodeModel> _byNodeId = new();
	private readonly Dictionary<string, SessionNodeModel> _byFileName = new();
	private readonly ISessionFileService _fileService;

	public List<SessionSummaryModel> Summaries { get; } = new();
	public List<(string nodeId, string prompt, TurnSource source)> RunTurnCalls { get; } = new();
	public string? AssistantReply { get; set; }

	/// <summary>False simulates a turn that was refused before it ran: nothing is appended to the transcript.</summary>
	public bool AppendsEntries { get; set; } = true;
	public TurnResultModel RunResult { get; set; } = new(AssistantText: "ok", SessionId: null);
	public bool CancelResult { get; set; }
	public string? LastCancelNodeId { get; private set; }

	public FakeDaemonClientForTelegram(ISessionFileService fileService) => _fileService = fileService;

	public void AddNode(SessionNodeModel node)
	{
		_byNodeId[node.NodeId] = node;
		_byFileName[node.FileName] = node;
	}

	public event EventHandler<TreeChangedEventArgs>? TreeChanged { add { } remove { } }
	public event EventHandler? ScheduleChanged { add { } remove { } }
	public event EventHandler<SessionEntryAppendedEventArgs>? EntryAppended;
	public event EventHandler<SessionTurnCompletedEventArgs>? TurnCompleted;
	public event EventHandler<SessionIndexedEventArgs>? SessionIndexed { add { } remove { } }
	public event EventHandler<AttentionChangedEventArgs>? AttentionChanged { add { } remove { } }
	public event EventHandler<SessionTagsChangedEventArgs>? SessionTagsChanged { add { } remove { } }

	public void RaiseTurnCompleted(string fileName, bool cancelled = false, string? errorText = null) =>
		TurnCompleted?.Invoke(this, new SessionTurnCompletedEventArgs { FileName = fileName, Cancelled = cancelled, ErrorText = errorText });

	public void RaiseEntryAppended(string fileName, string role, string content) =>
		EntryAppended?.Invoke(this, new SessionEntryAppendedEventArgs
		{
			FileName = fileName,
			Entry = new SessionEntryModel { Timestamp = DateTimeOffset.UtcNow, Role = role, Content = content },
		});

	public IReadOnlyList<DirectoryNodeModel> Directories => Array.Empty<DirectoryNodeModel>();
	public string? SelectedNodeId { get; set; }

	public SessionNodeModel? FindSessionByNodeId(string nodeId) => _byNodeId.TryGetValue(nodeId, out var n) ? n : null;
	public SessionNodeModel? FindSessionByAgentToken(string token) => throw new NotImplementedException();
	public SessionNodeModel? FindSessionByFileName(string fileName) => _byFileName.TryGetValue(fileName, out var n) ? n : null;
	public (string directoryLabel, string treePath) GetSessionLocation(SessionNodeModel session) => throw new NotImplementedException();

	public bool IsSessionRunning(string nodeId) => throw new NotImplementedException();
	public void SetSessionRunning(string nodeId, bool isRunning) => throw new NotImplementedException();

	public SemaphoreSlim GetTurnLock(string nodeId) => throw new NotImplementedException();

	public Task<TurnResultModel> RunTurnAsync(SessionNodeModel node, string prompt, TurnSource source, CancellationToken cancellationToken = default)
	{
		RunTurnCalls.Add((node.NodeId, prompt, source));

		// Mirrors SessionTurnService.RunTurnAsync's real append order: user prompt first, then the
		// assistant's reply (if configured), both via the same EntryAppended tap the mirror uses.
		if (AppendsEntries)
			RaiseEntryAppended(node.FileName, Constants.SessionFile.RoleUser, prompt);
		if (AppendsEntries && AssistantReply != null)
			RaiseEntryAppended(node.FileName, Constants.SessionFile.RoleAssistant, AssistantReply);

		return Task.FromResult(RunResult);
	}

	public bool CancelTurn(string nodeId) { LastCancelNodeId = nodeId; return CancelResult; }
	public string? ResolveProfileName(SessionNodeModel node) => throw new NotImplementedException();

	public IReadOnlyList<ScheduleModel> GetSchedules(string? targetNodeId = null) => throw new NotImplementedException();
	public void AddSchedule(ScheduleModel schedule) => throw new NotImplementedException();
	public bool RemoveSchedule(string scheduleId) => throw new NotImplementedException();

	public IReadOnlyList<SessionEntryModel> ReadEntries(string fileName) => _fileService.ReadEntries(fileName);
	public bool SessionFileExists(string fileName) => throw new NotImplementedException();
	public string GetSessionFilePath(string fileName) => throw new NotImplementedException();

	public void LoadFromSettings() => throw new NotImplementedException();

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
	public bool MoveSession(SessionNodeModel session, DirectoryNodeModel targetDirectory, GroupNodeModel? targetGroup, int index) => throw new System.NotSupportedException();
	public void RaiseTreeModified() { }
	public void RaiseDaemonTreeMutation() { }

	public List<SessionSummaryModel> BuildAgentSessionSummaries() => Summaries;

	// Session Tags (FR.24) — not used by the Telegram bridge.
	public IReadOnlyList<SessionTagModel> GetSessionTags(string nodeId) => throw new NotImplementedException();
	public bool TakeTagGuidanceDue(string nodeId) => throw new NotImplementedException();
	public IReadOnlyList<SessionTagUsageModel> GetSessionTagIndex(string? directoryPath) => throw new NotImplementedException();

	// Attention Ledger (FR.23) — not used by the Telegram bridge.
	public IReadOnlyList<AttentionItemModel> GetAttentionItems(string nodeId) => throw new NotImplementedException();
	public AttentionSummaryModel GetAttentionSummary(string nodeId) => throw new NotImplementedException();
	public void MarkAttentionAnswered(string nodeId, IReadOnlyList<AttentionAnswerModel> answers) => throw new NotImplementedException();
	public void CloseAttentionByUser(string nodeId, string itemId) => throw new NotImplementedException();
	public IReadOnlyList<AttentionItemModel> TakeUnreportedAttentionClosures(string nodeId) => throw new NotImplementedException();
	public IReadOnlySet<string> CollectAllClaudeSessionIds() => throw new NotImplementedException();

	public Task<int> ReindexSessionsAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
	public Task<IReadOnlyList<SessionSearchResultModel>> SearchSessionsAsync(
		string query, string? projectSlug = null, string? role = null,
		int? limit = null, int? offset = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
	public Task<IReadOnlySet<string>> SearchMatchingSessionIdsAsync(string query, string? projectSlug = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
	public Task<IReadOnlyList<SessionSearchScoreModel>> SearchSessionScoresAsync(string query, string? projectSlug = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
	public Task<bool> IndexSessionAsync(string externalId, string workingDirectory, CancellationToken cancellationToken = default) => throw new NotImplementedException();
	public Task<IReadOnlyDictionary<string, string>> EnsureTitlesAsync(
		IReadOnlyList<ClaudeSessionSummaryModel> summaries,
		Action<IReadOnlyDictionary<string, string>>? onBatchComplete = null,
		CancellationToken cancellationToken = default) => throw new NotImplementedException();
	public Task<IReadOnlyList<ClaudeSessionSummaryModel>> GetSessionSummariesAsync(
		IReadOnlyList<string> externalIds, CancellationToken cancellationToken = default) => throw new NotImplementedException();
}

// ── Tests ──────────────────────────────────────────────────────────────────────

/// <remarks>Created by Claude</remarks>
public sealed partial class TelegramBridgeServiceTests : IDisposable
{
	private readonly string _tempDir;
	private readonly AppSettingsService _appSettings;
	private readonly FakeTelegramClientForBridge _client;
	private readonly SessionFileService _fileService;
	private readonly FakeDaemonClientForTelegram _daemon;
	private readonly FakeDirectoryLabelServiceForTelegram _labelService;
	private TelegramBridgeService? _service;

	public TelegramBridgeServiceTests()
	{
		_tempDir = Path.Combine(Path.GetTempPath(), $"telegram-bridge-tests-{Guid.NewGuid():N}");
		Directory.CreateDirectory(_tempDir);
		_appSettings = new AppSettingsService(_tempDir);
		_appSettings.Settings.SessionFilesRoot = _tempDir;
		_appSettings.Settings.TelegramEnabled = true;
		_appSettings.Settings.TelegramChatId = -1001;
		_appSettings.Settings.TelegramAllowedUserIds = new List<long> { 555 };
		_appSettings.Settings.TelegramMirrorMode = TelegramMirrorMode.Full; // the FR.21.9 mirror tests; mode-specific tests override

		_client = new FakeTelegramClientForBridge();
		_fileService = new SessionFileService(_appSettings);
		_daemon = new FakeDaemonClientForTelegram(_fileService);
		_labelService = new FakeDirectoryLabelServiceForTelegram();
	}

	public void Dispose()
	{
		_service?.Stop();
		_service?.ReconciliationTask.Wait(TimeSpan.FromSeconds(5)); // its final settings save must land before the temp dir goes
		if (Directory.Exists(_tempDir))
			Directory.Delete(_tempDir, recursive: true);
	}

	private TelegramBridgeService BuildService() =>
		_service = new TelegramBridgeService(_appSettings, _client, _daemon, _labelService,
			new TelegramInbox(_client, Path.Combine(_tempDir, "inbox")));

	private static SessionNodeModel MakeNode(string nodeId, string name = "Session", int orchestrationDepth = 0) =>
		new() { NodeId = nodeId, Name = name, FileName = $"{nodeId}.txt", WorkingDirectory = @"C:\proj", OrchestrationDepth = orchestrationDepth };

	// ── EnsureTopicForSessionAsync ───────────────────────────────────────────────

	[Fact]
	public async Task EnsureTopicForSessionAsync_FirstCall_CreatesTopicAndPersistsMapping()
	{
		var svc = BuildService();
		var node = MakeNode("node-1", "My Session");

		await svc.EnsureTopicForSessionAsync(node);

		Assert.Single(_client.CreatedTopics);
		Assert.Equal("TestDir / My Session", _client.CreatedTopics[0].name);
		Assert.True(_appSettings.Settings.TelegramTopicThreadIdsByNodeId.ContainsKey("node-1"));
	}

	[Fact]
	public async Task EnsureTopicForSessionAsync_SecondCall_DoesNotCreateTopicAgain()
	{
		var svc = BuildService();
		var node = MakeNode("node-1");

		await svc.EnsureTopicForSessionAsync(node);
		await svc.EnsureTopicForSessionAsync(node);

		Assert.Single(_client.CreatedTopics);
	}

	[Fact]
	public async Task EnsureTopicForSessionAsync_TelegramDisabled_DoesNotCreateTopic()
	{
		_appSettings.Settings.TelegramEnabled = false;
		var svc = BuildService();
		var node = MakeNode("node-1");

		await svc.EnsureTopicForSessionAsync(node);

		Assert.Empty(_client.CreatedTopics);
	}

	// ── NotifyAsync ───────────────────────────────────────────────────────────────

	[Fact]
	public async Task NotifyAsync_SessionWithNoTopicYet_CreatesTopicThenSends()
	{
		var svc = BuildService();
		var node = MakeNode("node-1", "My Session");
		_daemon.AddNode(node);

		var sent = await svc.NotifyAsync("node-1", "hello world");

		Assert.True(sent);
		Assert.Single(_client.CreatedTopics);
		Assert.Single(_client.SentMessages);
		Assert.Equal("hello world", _client.SentMessages[0].text);
		Assert.Equal(100, _client.SentMessages[0].threadId);
	}

	[Fact]
	public async Task NotifyAsync_PrefixesWithMentionHandle_WhenConfigured()
	{
		_appSettings.Settings.TelegramMentionHandle = "@harmlessevil";
		var svc = BuildService();
		var node = MakeNode("node-1", "My Session");
		_daemon.AddNode(node);

		var sent = await svc.NotifyAsync("node-1", "hello world");

		Assert.True(sent);
		Assert.Equal("@harmlessevil hello world", _client.SentMessages[0].text);
	}

	[Fact]
	public async Task NotifyAsync_TelegramDisabled_IsNoOp_DoesNotThrow()
	{
		_appSettings.Settings.TelegramEnabled = false;
		var svc = BuildService();

		var sent = await svc.NotifyAsync("node-1", "hello");

		Assert.False(sent);
		Assert.Empty(_client.SentMessages);
	}

	[Fact]
	public async Task NotifyAsync_UnknownNodeId_ReturnsFalse_DoesNotThrow()
	{
		var svc = BuildService();

		var sent = await svc.NotifyAsync("does-not-exist", "hello");

		Assert.False(sent);
	}

	// ── Universal I/O mirror (FR.21.9) ────────────────────────────────────────────

	[Fact]
	public async Task EntryAppended_AssistantEntry_MirrorsToTopic_NoPrefix()
	{
		var node = MakeNode("node-1");
		_daemon.AddNode(node);
		_appSettings.Settings.TelegramTopicThreadIdsByNodeId["node-1"] = 42;
		var svc = BuildService();
		svc.Start();

		_daemon.RaiseEntryAppended(node.FileName, Constants.SessionFile.RoleAssistant, "Hello from Claude");

		await WaitUntil(() => _client.SentMessages.Count > 0);
		Assert.Equal("Hello from Claude", _client.SentMessages[0].text);
		Assert.Equal(42, _client.SentMessages[0].threadId);
	}

	[Fact]
	public async Task EntryAppended_UserEntry_MirrorsWithUserPrefix()
	{
		var node = MakeNode("node-1");
		_daemon.AddNode(node);
		_appSettings.Settings.TelegramTopicThreadIdsByNodeId["node-1"] = 42;
		var svc = BuildService();
		svc.Start();

		_daemon.RaiseEntryAppended(node.FileName, Constants.SessionFile.RoleUser, "typed on desktop");

		await WaitUntil(() => _client.SentMessages.Count > 0);
		Assert.Equal("USER: typed on desktop", _client.SentMessages[0].text);
	}

	[Fact]
	public async Task EntryAppended_SystemEntry_MirrorsWithGearPrefix()
	{
		var node = MakeNode("node-1");
		_daemon.AddNode(node);
		_appSettings.Settings.TelegramTopicThreadIdsByNodeId["node-1"] = 42;
		var svc = BuildService();
		svc.Start();

		_daemon.RaiseEntryAppended(node.FileName, Constants.SessionFile.RoleSystem, "Turn cancelled");

		await WaitUntil(() => _client.SentMessages.Count > 0);
		Assert.Equal("⚙️ Turn cancelled", _client.SentMessages[0].text);
	}

	[Fact]
	public async Task EntryAppended_CompactionEntry_NotMirrored()
	{
		var node = MakeNode("node-1");
		_daemon.AddNode(node);
		_appSettings.Settings.TelegramTopicThreadIdsByNodeId["node-1"] = 42;
		var svc = BuildService();
		svc.Start();

		_daemon.RaiseEntryAppended(node.FileName, Constants.SessionFile.RoleCompaction, string.Empty);

		await Task.Delay(200);
		Assert.Empty(_client.SentMessages);
	}

	[Fact]
	public async Task EntryAppended_UnknownFileName_DoesNothing()
	{
		var svc = BuildService();
		svc.Start();

		_daemon.RaiseEntryAppended("unknown.txt", Constants.SessionFile.RoleAssistant, "hi");

		await Task.Delay(200);
		Assert.Empty(_client.SentMessages);
	}

	[Fact]
	public async Task EntryAppended_TelegramDisabled_DoesNothing()
	{
		var node = MakeNode("node-1");
		_daemon.AddNode(node);
		_appSettings.Settings.TelegramTopicThreadIdsByNodeId["node-1"] = 42;
		var svc = BuildService();
		svc.Start();
		_appSettings.Settings.TelegramEnabled = false;

		_daemon.RaiseEntryAppended(node.FileName, Constants.SessionFile.RoleAssistant, "hi");

		await Task.Delay(200);
		Assert.Empty(_client.SentMessages);
	}

	[Fact]
	public async Task EntryAppended_LongContent_IsChunkedAcrossMultipleMessages()
	{
		var node = MakeNode("node-1");
		_daemon.AddNode(node);
		_appSettings.Settings.TelegramTopicThreadIdsByNodeId["node-1"] = 42;
		var svc = BuildService();
		svc.Start();
		var longContent = string.Concat(Enumerable.Repeat("a", 5000));

		_daemon.RaiseEntryAppended(node.FileName, Constants.SessionFile.RoleAssistant, longContent);

		await WaitUntil(() => _client.SentMessages.Count >= 2);
		Assert.True(_client.SentMessages.All(m => m.text.Length <= Constants.Telegram.MaxMessageLength));
		Assert.Equal(longContent, string.Concat(_client.SentMessages.Select(m => m.text)));
	}

	// ── Inbound routing (via Start() + canned update batches) ───────────────────

	[Fact]
	public async Task HandleUpdate_MessageInMappedTopic_RunsTurnOnCorrectSession()
	{
		var node = MakeNode("node-1", "My Session");
		_daemon.AddNode(node);
		_appSettings.Settings.TelegramTopicThreadIdsByNodeId["node-1"] = 42;

		_client.UpdateBatches.Enqueue(new[]
		{
			new TelegramUpdate(UpdateId: 1, ChatId: -1001, FromUserId: 555, Text: "do the thing", MessageThreadId: 42),
		});

		var svc = BuildService();
		svc.Start();
		await WaitUntil(() => _daemon.RunTurnCalls.Count > 0);

		Assert.Contains(_daemon.RunTurnCalls, c => c.nodeId == "node-1" && c.prompt == "do the thing");
	}

	/// <summary>ISSUE.013 P3: a turn refused before it ran writes nothing to the topic: the user gets the usual "Turn failed" message and the echo suppression is cleared.</summary>
	[Fact]
	public async Task HandleUpdate_RefusedTurn_SendsTurnFailed_AndClearsTheEchoSuppression()
	{
		var node = MakeNode("node-1", "My Session");
		_daemon.AddNode(node);
		_appSettings.Settings.TelegramTopicThreadIdsByNodeId["node-1"] = 42;
		_daemon.AppendsEntries = false;
		_daemon.RunResult = new TurnResultModel(string.Empty, null, IsError: true,
			ErrorMessage: "Session 'My Session' is pinned to profile 'Retired', which no longer exists (removed or renamed).", Refused: true);
		_client.UpdateBatches.Enqueue(new[] { new TelegramUpdate(1, ChatId: -1001, FromUserId: 555, Text: "do the thing", MessageThreadId: 42) });

		var svc = BuildService();
		svc.Start();
		await WaitUntil(() => _client.SentMessages.Count > 0);

		Assert.Single(_client.SentMessages);
		Assert.StartsWith("\u26a0\ufe0f Turn failed: Session 'My Session' is pinned to profile 'Retired'", _client.SentMessages[0].text);
		var suppress = (System.Collections.Concurrent.ConcurrentDictionary<string, string>)typeof(TelegramBridgeService)
			.GetField("_suppressNextUserAppend", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(svc)!;
		Assert.False(suppress.ContainsKey("node-1"));
	}

	[Fact]
	public async Task HandleUpdate_TurnThatRanOnDefaultBecauseTheRecordedProfileIsGone_SaysSo()
	{
		var node = MakeNode("node-1", "My Session");
		_daemon.AddNode(node);
		_appSettings.Settings.TelegramTopicThreadIdsByNodeId["node-1"] = 42;
		_daemon.RunResult = new TurnResultModel("ok", null, Note: "the recorded profile 'Retired' no longer exists");
		_client.UpdateBatches.Enqueue(new[] { new TelegramUpdate(1, ChatId: -1001, FromUserId: 555, Text: "do the thing", MessageThreadId: 42) });

		var svc = BuildService();
		svc.Start();
		await WaitUntil(() => _client.SentMessages.Count > 0);

		Assert.Contains("ran on the Default profile: the recorded profile 'Retired' no longer exists", _client.SentMessages[0].text);
	}

	[Fact]
	public async Task HandleUpdate_MessageInMappedTopic_SuppressesEchoOfOwnMessage_ButMirrorsAssistantReply()
	{
		var node = MakeNode("node-1", "My Session");
		_daemon.AddNode(node);
		_appSettings.Settings.TelegramTopicThreadIdsByNodeId["node-1"] = 42;
		_daemon.AssistantReply = "Sure, done.";

		_client.UpdateBatches.Enqueue(new[]
		{
			new TelegramUpdate(1, ChatId: -1001, FromUserId: 555, Text: "do the thing", MessageThreadId: 42),
		});

		var svc = BuildService();
		svc.Start();
		await WaitUntil(() => _client.SentMessages.Count > 0);

		// Only the assistant reply is mirrored — the routed user message is echo-suppressed since
		// it is already visible as the user's own message in the topic.
		Assert.Single(_client.SentMessages);
		Assert.Equal("Sure, done.", _client.SentMessages[0].text);
	}

	[Fact]
	public async Task HandleUpdate_UnauthorizedUserId_DropsMessage_NoTurnRun()
	{
		var node = MakeNode("node-1");
		_daemon.AddNode(node);
		_appSettings.Settings.TelegramTopicThreadIdsByNodeId["node-1"] = 42;

		_client.UpdateBatches.Enqueue(new[]
		{
			new TelegramUpdate(1, ChatId: -1001, FromUserId: 999 /* not allowed */, Text: "hack", MessageThreadId: 42),
		});

		var svc = BuildService();
		svc.Start();
		await Task.Delay(200);

		Assert.Empty(_daemon.RunTurnCalls);
		Assert.Empty(_client.SentMessages);
	}

	[Fact]
	public async Task HandleUpdate_WrongChatId_DropsMessage_NoTurnRun()
	{
		var node = MakeNode("node-1");
		_daemon.AddNode(node);
		_appSettings.Settings.TelegramTopicThreadIdsByNodeId["node-1"] = 42;

		_client.UpdateBatches.Enqueue(new[]
		{
			new TelegramUpdate(1, ChatId: -9999 /* wrong chat */, FromUserId: 555, Text: "hi", MessageThreadId: 42),
		});

		var svc = BuildService();
		svc.Start();
		await Task.Delay(200);

		Assert.Empty(_daemon.RunTurnCalls);
	}

	[Fact]
	public async Task HandleUpdate_SessionsCommand_ListsFavoritedAndTopicMappedSessionsOnly()
	{
		_daemon.Summaries.AddRange(new[]
		{
			new SessionSummaryModel("node-fav", "Favorite", "Dir", @"C:\a", IsRunning: false, IsResumable: true, IsFavorite: true),
			new SessionSummaryModel("node-topic", "Active", "Dir", @"C:\b", IsRunning: true, IsResumable: true, IsFavorite: false),
			new SessionSummaryModel("node-other", "Untouched", "Dir", @"C:\c", IsRunning: false, IsResumable: false, IsFavorite: false),
		});
		_appSettings.Settings.TelegramTopicThreadIdsByNodeId["node-topic"] = 7;

		_client.UpdateBatches.Enqueue(new[]
		{
			new TelegramUpdate(1, ChatId: -1001, FromUserId: 555, Text: "/sessions", MessageThreadId: null),
		});

		var svc = BuildService();
		svc.Start();
		await WaitUntil(() => _client.SentMessages.Count > 0);

		var reply = _client.SentMessages[0].text;
		Assert.Contains("Favorite", reply);
		Assert.Contains("Active", reply);
		Assert.DoesNotContain("Untouched", reply);
	}

	[Fact]
	public async Task HandleUpdate_OpenCommand_ReturnsLastEntriesForMatchedSession_EvenWithoutTopic()
	{
		var node = MakeNode("node-open", "OpenMe");
		_daemon.AddNode(node);
		_daemon.Summaries.Add(new SessionSummaryModel(
			"node-open", "OpenMe", "Dir", @"C:\a", IsRunning: false, IsResumable: false));
		_fileService.AppendMessage(node.FileName, Constants.SessionFile.RoleUser, "hello there");
		_fileService.AppendMessage(node.FileName, Constants.SessionFile.RoleAssistant, "hi back");

		_client.UpdateBatches.Enqueue(new[]
		{
			new TelegramUpdate(1, ChatId: -1001, FromUserId: 555, Text: "/open OpenMe", MessageThreadId: null),
		});

		var svc = BuildService();
		svc.Start();
		await WaitUntil(() => _client.SentMessages.Count > 0);

		var reply = _client.SentMessages[0].text;
		Assert.Contains("hello there", reply);
		Assert.Contains("hi back", reply);
		Assert.Empty(_client.CreatedTopics); // /open must not create a topic as a side effect
	}

	[Fact]
	public async Task HandleUpdate_UnmappedThreadAndUnrecognizedCommand_RepliesWithUnrecognizedMessage()
	{
		_client.UpdateBatches.Enqueue(new[]
		{
			new TelegramUpdate(1, ChatId: -1001, FromUserId: 555, Text: "gibberish", MessageThreadId: 999 /* unmapped */),
		});

		var svc = BuildService();
		svc.Start();
		await WaitUntil(() => _client.SentMessages.Count > 0);

		Assert.Contains("Unrecognized", _client.SentMessages[0].text);
		Assert.Empty(_daemon.RunTurnCalls);
	}

	// ── /stop (FR.21.11) ───────────────────────────────────────────────────────────

	[Fact]
	public async Task HandleUpdate_StopCommand_InMappedTopic_CancelsRunningTurn()
	{
		_appSettings.Settings.TelegramTopicThreadIdsByNodeId["node-1"] = 42;
		_daemon.CancelResult = true;

		_client.UpdateBatches.Enqueue(new[]
		{
			new TelegramUpdate(1, ChatId: -1001, FromUserId: 555, Text: "/stop", MessageThreadId: 42),
		});

		var svc = BuildService();
		svc.Start();
		await WaitUntil(() => _client.SentMessages.Count > 0);

		Assert.Equal("node-1", _daemon.LastCancelNodeId);
		Assert.Contains("cancelled", _client.SentMessages[0].text, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public async Task HandleUpdate_StopCommand_NoRunningTurn_RepliesNoTurnRunning()
	{
		_appSettings.Settings.TelegramTopicThreadIdsByNodeId["node-1"] = 42;
		_daemon.CancelResult = false;

		_client.UpdateBatches.Enqueue(new[]
		{
			new TelegramUpdate(1, ChatId: -1001, FromUserId: 555, Text: "/stop", MessageThreadId: 42),
		});

		var svc = BuildService();
		svc.Start();
		await WaitUntil(() => _client.SentMessages.Count > 0);

		Assert.Contains("No turn is currently running", _client.SentMessages[0].text);
	}

	// ── Worker Session exclusion (FR.21.19) ─────────────────────────────────────

	[Fact]
	public async Task EnsureTopicForSessionAsync_WorkerSession_DoesNotCreateTopic()
	{
		var svc = BuildService();
		var node = MakeNode("worker-1", "Worker", orchestrationDepth: 1);

		await svc.EnsureTopicForSessionAsync(node);

		Assert.Empty(_client.CreatedTopics);
		Assert.False(_appSettings.Settings.TelegramTopicThreadIdsByNodeId.ContainsKey("worker-1"));
	}

	[Fact]
	public async Task NotifyAsync_WorkerSession_ReturnsFalse_NoTopicNoSend()
	{
		var svc = BuildService();
		var node = MakeNode("worker-1", "Worker", orchestrationDepth: 1);
		_daemon.AddNode(node);

		var sent = await svc.NotifyAsync("worker-1", "a question from a subagent");

		Assert.False(sent);
		Assert.Empty(_client.CreatedTopics);
		Assert.Empty(_client.SentMessages);
	}

	[Fact]
	public async Task EntryAppended_WorkerSession_DoesNotMirror()
	{
		var node = MakeNode("worker-1", "Worker", orchestrationDepth: 1);
		_daemon.AddNode(node);
		var svc = BuildService();
		svc.Start();

		_daemon.RaiseEntryAppended(node.FileName, Constants.SessionFile.RoleAssistant, "worker output");

		await Task.Delay(200);
		Assert.Empty(_client.SentMessages);
		Assert.Empty(_client.CreatedTopics);
	}

	[Fact]
	public async Task HandleUpdate_SessionsCommand_ExcludesWorkerSessionsEvenIfFavorited()
	{
		_daemon.Summaries.AddRange(new[]
		{
			new SessionSummaryModel("node-top", "Supervisor", "Dir", @"C:\a", IsRunning: true, IsResumable: true, IsFavorite: true, OrchestrationDepth: 0),
			new SessionSummaryModel("node-worker", "Worker", "Dir", @"C:\b", IsRunning: true, IsResumable: true, IsFavorite: true, OrchestrationDepth: 1),
		});
		_appSettings.Settings.TelegramTopicThreadIdsByNodeId["node-worker"] = 9;

		_client.UpdateBatches.Enqueue(new[]
		{
			new TelegramUpdate(1, ChatId: -1001, FromUserId: 555, Text: "/sessions", MessageThreadId: null),
		});

		var svc = BuildService();
		svc.Start();
		await WaitUntil(() => _client.SentMessages.Count > 0);

		var reply = _client.SentMessages[0].text;
		Assert.Contains("Supervisor", reply);
		Assert.DoesNotContain("Worker", reply);
	}

	[Fact]
	public async Task Start_DeletesExistingTopicsForWorkerSessions_AndRemovesMappings()
	{
		var supervisor = MakeNode("node-top", "Supervisor", orchestrationDepth: 0);
		var worker = MakeNode("node-worker", "Worker", orchestrationDepth: 1);
		_daemon.AddNode(supervisor);
		_daemon.AddNode(worker);
		_appSettings.Settings.TelegramTopicThreadIdsByNodeId["node-top"] = 5;
		_appSettings.Settings.TelegramTopicThreadIdsByNodeId["node-worker"] = 9;
		_appSettings.Settings.TelegramTopicNamesByNodeId["node-worker"] = "Dir / Worker";

		var svc = BuildService();
		svc.Start();
		await svc.ReconciliationTask;

		Assert.Contains((-1001L, 9), _client.DeletedTopics);
		Assert.False(_appSettings.Settings.TelegramTopicThreadIdsByNodeId.ContainsKey("node-worker"));
		Assert.False(_appSettings.Settings.TelegramTopicNamesByNodeId.ContainsKey("node-worker"));
		Assert.True(_appSettings.Settings.TelegramTopicThreadIdsByNodeId.ContainsKey("node-top")); // untouched
	}

	[Fact]
	public async Task Start_WorkerTopicDeleteFails_LeavesMappingForRetry()
	{
		var worker = MakeNode("node-worker", "Worker", orchestrationDepth: 1);
		_daemon.AddNode(worker);
		_appSettings.Settings.TelegramTopicThreadIdsByNodeId["node-worker"] = 9;
		_client.DeleteBehavior = (_, _) => throw new TelegramApiException(500, "internal error", null);

		var svc = BuildService();
		svc.Start();
		await svc.ReconciliationTask;

		Assert.True(_appSettings.Settings.TelegramTopicThreadIdsByNodeId.ContainsKey("node-worker"));
	}

	// ── Silent delivery (FR.21.21) ────────────────────────────────────────────────

	private SessionNodeModel StartMirror(TelegramMirrorMode mode, int orchestrationDepth = 0)
	{
		_appSettings.Settings.TelegramMirrorMode = mode;
		var node = MakeNode("node-1", orchestrationDepth: orchestrationDepth);
		_daemon.AddNode(node);
		_appSettings.Settings.TelegramTopicThreadIdsByNodeId["node-1"] = 42;
		BuildService().Start();
		return node;
	}

	[Fact]
	public async Task NotifyAsync_SendsWithSound_AndMentionPrefix()
	{
		_appSettings.Settings.TelegramMentionHandle = "@harmlessevil";
		var svc = BuildService();
		_daemon.AddNode(MakeNode("node-1"));

		await svc.NotifyAsync("node-1", "hello");

		Assert.Single(_client.SentMessages);
		Assert.False(_client.SentMessages[0].disableNotification);
		Assert.Equal("@harmlessevil hello", _client.SentMessages[0].text);
	}

	[Fact]
	public async Task Mirror_FullMode_EveryPostIsSilent()
	{
		var node = StartMirror(TelegramMirrorMode.Full);

		_daemon.RaiseEntryAppended(node.FileName, Constants.SessionFile.RoleUser, "typed");
		_daemon.RaiseEntryAppended(node.FileName, Constants.SessionFile.RoleSystem, "note");
		_daemon.RaiseEntryAppended(node.FileName, Constants.SessionFile.RoleAssistant, new string('a', 5000));

		await WaitUntil(() => _client.SentMessages.Count >= 4);
		Assert.All(_client.SentMessages, m =>
		{
			Assert.True(m.disableNotification);
			Assert.DoesNotContain("@", m.text);
		});
	}

	[Fact]
	public async Task Mirror_AnswersOnlyMode_AnswerPostIsSilent()
	{
		var node = StartMirror(TelegramMirrorMode.AnswersOnly);

		_daemon.RaiseEntryAppended(node.FileName, Constants.SessionFile.RoleAssistant, "answer");
		_daemon.RaiseTurnCompleted(node.FileName);

		await WaitUntil(() => _client.SentMessages.Count == 1);
		Assert.True(_client.SentMessages[0].disableNotification);
	}

	[Fact]
	public async Task HandleUpdate_CommandReplies_AreSilent()
	{
		_daemon.Summaries.Add(new SessionSummaryModel("node-1", "Favorite", "Dir", @"C:\a", IsRunning: false, IsResumable: true, IsFavorite: true));
		_daemon.AddNode(MakeNode("node-1", "Favorite"));
		_appSettings.Settings.TelegramTopicThreadIdsByNodeId["node-1"] = 42;
		_client.UpdateBatches.Enqueue(new[]
		{
			new TelegramUpdate(1, ChatId: -1001, FromUserId: 555, Text: "/sessions", MessageThreadId: null),
			new TelegramUpdate(2, ChatId: -1001, FromUserId: 555, Text: "/open Favorite", MessageThreadId: null),
			new TelegramUpdate(3, ChatId: -1001, FromUserId: 555, Text: "/stop", MessageThreadId: 42),
			new TelegramUpdate(4, ChatId: -1001, FromUserId: 555, Text: "gibberish", MessageThreadId: 999),
		});

		BuildService().Start();
		await WaitUntil(() => _client.SentMessages.Count >= 4);

		Assert.All(_client.SentMessages, m => Assert.True(m.disableNotification));
	}

	// ── Mirror modes (FR.21.22) ───────────────────────────────────────────────────

	[Fact]
	public async Task AnswersOnly_TurnWithSeveralAssistantEntries_PostsOnlyTheLastOnTurnCompleted()
	{
		var node = StartMirror(TelegramMirrorMode.AnswersOnly);

		_daemon.RaiseEntryAppended(node.FileName, Constants.SessionFile.RoleAssistant, "one");
		_daemon.RaiseEntryAppended(node.FileName, Constants.SessionFile.RoleAssistant, "two");
		_daemon.RaiseEntryAppended(node.FileName, Constants.SessionFile.RoleAssistant, "three");
		await Task.Delay(200);
		Assert.Empty(_client.SentMessages);

		_daemon.RaiseTurnCompleted(node.FileName);

		await WaitUntil(() => _client.SentMessages.Count > 0);
		await Task.Delay(200);
		Assert.Single(_client.SentMessages);
		Assert.Equal("three", _client.SentMessages[0].text);
	}

	[Fact]
	public async Task AnswersOnly_UserAndSystemEntries_NotPosted()
	{
		var node = StartMirror(TelegramMirrorMode.AnswersOnly);

		_daemon.RaiseEntryAppended(node.FileName, Constants.SessionFile.RoleUser, "typed");
		_daemon.RaiseEntryAppended(node.FileName, Constants.SessionFile.RoleSystem, "note");
		_daemon.RaiseTurnCompleted(node.FileName);

		await Task.Delay(200);
		Assert.Empty(_client.SentMessages);
	}

	[Fact]
	public async Task AnswersOnly_CancelledTurn_PostsNothingAndClearsPending()
	{
		var node = StartMirror(TelegramMirrorMode.AnswersOnly);

		_daemon.RaiseEntryAppended(node.FileName, Constants.SessionFile.RoleAssistant, "half an answer");
		_daemon.RaiseTurnCompleted(node.FileName, cancelled: true);
		await Task.Delay(200);
		Assert.Empty(_client.SentMessages);

		_daemon.RaiseTurnCompleted(node.FileName); // nothing left pending from the cancelled turn
		_daemon.RaiseEntryAppended(node.FileName, Constants.SessionFile.RoleAssistant, "next answer");
		_daemon.RaiseTurnCompleted(node.FileName);

		await WaitUntil(() => _client.SentMessages.Count > 0);
		await Task.Delay(200);
		Assert.Single(_client.SentMessages);
		Assert.Equal("next answer", _client.SentMessages[0].text);
	}

	[Fact]
	public async Task AnswersOnly_TurnCompletedWithoutAssistantEntry_PostsNothing()
	{
		var node = StartMirror(TelegramMirrorMode.AnswersOnly);

		_daemon.RaiseTurnCompleted(node.FileName);

		await Task.Delay(200);
		Assert.Empty(_client.SentMessages);
	}

	[Fact]
	public async Task AnswersOnly_UsageLimitTurn_PostsLimitTextOnce()
	{
		var node = StartMirror(TelegramMirrorMode.AnswersOnly);
		const string limit = "You've hit your session limit · resets 1:50pm";

		_daemon.RaiseEntryAppended(node.FileName, Constants.SessionFile.RoleAssistant, limit);
		_daemon.RaiseEntryAppended(node.FileName, Constants.SessionFile.RoleSystem, limit);
		_daemon.RaiseTurnCompleted(node.FileName, errorText: limit + " ");

		await WaitUntil(() => _client.SentMessages.Count > 0);
		await Task.Delay(200);
		Assert.Single(_client.SentMessages);
		Assert.Equal(limit, _client.SentMessages[0].text);
		Assert.True(_client.SentMessages[0].disableNotification);
	}

	[Fact]
	public async Task AnswersOnly_ErrorTextWithoutAssistantEntry_PostsErrorTextWithGearPrefix()
	{
		var node = StartMirror(TelegramMirrorMode.AnswersOnly);

		_daemon.RaiseTurnCompleted(node.FileName, errorText: "boom");

		await WaitUntil(() => _client.SentMessages.Count > 0);
		await Task.Delay(200);
		Assert.Single(_client.SentMessages);
		Assert.Equal("⚙️ boom", _client.SentMessages[0].text);
	}

	[Fact]
	public async Task AnswersOnly_AnswerThenDifferentErrorText_PostsBothInOrder()
	{
		var node = StartMirror(TelegramMirrorMode.AnswersOnly);

		_daemon.RaiseEntryAppended(node.FileName, Constants.SessionFile.RoleAssistant, "partial answer");
		_daemon.RaiseTurnCompleted(node.FileName, errorText: "boom");

		await WaitUntil(() => _client.SentMessages.Count >= 2);
		Assert.Equal("partial answer", _client.SentMessages[0].text);
		Assert.Equal("⚙️ boom", _client.SentMessages[1].text);
	}

	[Fact]
	public async Task AnswersOnly_EntryOutsideTurn_SupersededByNextTurnsAnswer()
	{
		var node = StartMirror(TelegramMirrorMode.AnswersOnly);

		_daemon.RaiseEntryAppended(node.FileName, Constants.SessionFile.RoleAssistant, "stray");
		await Task.Delay(200);
		Assert.Empty(_client.SentMessages);

		_daemon.RaiseEntryAppended(node.FileName, Constants.SessionFile.RoleAssistant, "real answer");
		_daemon.RaiseTurnCompleted(node.FileName);

		await WaitUntil(() => _client.SentMessages.Count > 0);
		await Task.Delay(200);
		Assert.Single(_client.SentMessages);
		Assert.Equal("real answer", _client.SentMessages[0].text);
	}

	[Fact]
	public async Task AnswersOnly_LongAnswer_ChunkedLikeFullMode()
	{
		var node = StartMirror(TelegramMirrorMode.AnswersOnly);
		var longContent = new string('a', 5000);

		_daemon.RaiseEntryAppended(node.FileName, Constants.SessionFile.RoleAssistant, longContent);
		_daemon.RaiseTurnCompleted(node.FileName);

		await WaitUntil(() => _client.SentMessages.Count >= 2);
		Assert.All(_client.SentMessages, m => Assert.True(m.text.Length <= Constants.Telegram.MaxMessageLength));
		Assert.Equal(longContent, string.Concat(_client.SentMessages.Select(m => m.text)));
	}

	[Fact]
	public void AnswersOnly_IsDefaultMode()
	{
		Assert.Equal(TelegramMirrorMode.AnswersOnly, new AppSettingsModel().TelegramMirrorMode);
	}

	[Fact]
	public async Task Off_PostsNothing_ButNotifyStillSends()
	{
		var node = StartMirror(TelegramMirrorMode.Off);

		_daemon.RaiseEntryAppended(node.FileName, Constants.SessionFile.RoleUser, "typed");
		_daemon.RaiseEntryAppended(node.FileName, Constants.SessionFile.RoleAssistant, "answer");
		_daemon.RaiseTurnCompleted(node.FileName, errorText: "boom");
		await Task.Delay(200);
		Assert.Empty(_client.SentMessages);

		var sent = await _service!.NotifyAsync("node-1", "ping");

		Assert.True(sent);
		Assert.Single(_client.SentMessages);
	}

	[Theory]
	[InlineData(TelegramMirrorMode.Full)]
	[InlineData(TelegramMirrorMode.AnswersOnly)]
	[InlineData(TelegramMirrorMode.Off)]
	public async Task TurnCompleted_WorkerSession_Ignored(TelegramMirrorMode mode)
	{
		var node = StartMirror(mode, orchestrationDepth: 1);

		_daemon.RaiseEntryAppended(node.FileName, Constants.SessionFile.RoleAssistant, "worker output");
		_daemon.RaiseTurnCompleted(node.FileName, errorText: "boom");

		await Task.Delay(200);
		Assert.Empty(_client.SentMessages);
	}

	// ── Mailbox Deliveries in Full mode (FR.21.23) ────────────────────────────────

	[Fact]
	public async Task Full_MailboxDeliveryEntry_PostedAsFirstLineWithGearPrefix()
	{
		var node = StartMirror(TelegramMirrorMode.Full);

		_daemon.RaiseEntryAppended(node.FileName, Constants.SessionFile.RoleUser,
			"[Scheduled] Worker 'W2' result\n[Worker finished] W2\nlong result text");

		await WaitUntil(() => _client.SentMessages.Count > 0);
		await Task.Delay(200);
		Assert.Single(_client.SentMessages);
		Assert.Equal("⚙️ [Scheduled] Worker 'W2' result", _client.SentMessages[0].text);
	}

	[Fact]
	public async Task Full_MergedMailboxDelivery_PostedAsOneLine()
	{
		var node = StartMirror(TelegramMirrorMode.Full);

		_daemon.RaiseEntryAppended(node.FileName, Constants.SessionFile.RoleUser,
			"[Scheduled] Worker results (2): W2, W3\n[Worker finished] W2\nresult 2\n[Worker finished] W3\nresult 3");

		await WaitUntil(() => _client.SentMessages.Count > 0);
		await Task.Delay(200);
		Assert.Single(_client.SentMessages);
		Assert.Equal("⚙️ [Scheduled] Worker results (2): W2, W3", _client.SentMessages[0].text);
	}

	[Fact]
	public async Task Full_OrdinaryUserEntry_StillPostedWithUserPrefix()
	{
		var node = StartMirror(TelegramMirrorMode.Full);

		_daemon.RaiseEntryAppended(node.FileName, Constants.SessionFile.RoleUser, "line one\nline two");

		await WaitUntil(() => _client.SentMessages.Count > 0);
		Assert.Equal("USER: line one\nline two", _client.SentMessages[0].text);
	}

	// ── Helpers ───────────────────────────────────────────────────────────────

	private static async Task WaitUntil(Func<bool> condition, int timeoutMs = 2000)
	{
		var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
		while (!condition() && DateTime.UtcNow < deadline)
			await Task.Delay(25);
		Assert.True(condition(), "Condition not met within timeout");
	}
}
