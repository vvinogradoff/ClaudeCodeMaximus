using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary>Records every argument of a stateless compaction call and plays back the configured events.</summary>
internal sealed class CompactionProcessManagerFake : IClaudeProcessManager
{
	public int ActiveProcessCount => 0;
	public List<CompactionCall> Calls { get; } = new();
	public Func<Action<ClaudeStreamEvent>, CancellationToken, Task>? Behaviour { get; set; }

	public async Task SendMessageAsync(
		string workingDirectory, string claudePath, string? sessionId, string userMessage,
		Action<ClaudeStreamEvent> onEvent, string? model = null, string? profileConfigDir = null,
		string? effort = null, string? mcpConfigPath = null, string? ollamaBaseUrl = null,
		bool disableTools = false, bool chrome = false, bool disallowNativeScheduling = false,
		LocalToolSpec? localToolSpec = null, bool strictMcpConfig = false,
		CancellationToken cancellationToken = default)
	{
		Calls.Add(new CompactionCall(workingDirectory, sessionId, userMessage, model, profileConfigDir, effort, mcpConfigPath,
			ollamaBaseUrl, disableTools, localToolSpec, strictMcpConfig));
		if (Behaviour != null)
			await Behaviour(onEvent, cancellationToken);
	}

	public Task<string?> RunPrintModeAsync(string claudePath, string prompt, string? model = null,
		string? profileConfigDir = null, int timeoutMs = 60000, CancellationToken cancellationToken = default) =>
		throw new NotSupportedException();

	public void TerminateAll() { }
}

internal sealed record CompactionCall(
	string WorkingDirectory, string? SessionId, string Prompt, string? Model, string? ProfileConfigDir, string? Effort,
	string? McpConfigPath, string? OllamaBaseUrl, bool DisableTools, LocalToolSpec? LocalToolSpec, bool StrictMcpConfig);

/// <summary>Only the per-node lock and the profile name matter to compaction.</summary>
internal sealed class CompactionTurnServiceFake : ISessionTurnService
{
	private readonly SemaphoreSlim _lock = new(1, 1);

	public string? ProfileName { get; set; }

	public SemaphoreSlim GetTurnLock(string nodeId) => _lock;
	public bool CancelTurn(string nodeId) => false;
	public string? ResolveProfileName(SessionNodeModel node) => ProfileName;
	public Task<TurnResultModel> RunTurnAsync(SessionNodeModel node, string prompt, TurnSource source, CancellationToken cancellationToken = default) =>
		throw new NotSupportedException();
	public Task<TurnResultModel> RunTurnAsync(SessionNodeModel node, string prompt, TurnSource source, TurnOptionsModel? options, CancellationToken cancellationToken = default) =>
		throw new NotSupportedException();
}

/// <summary>Prompt building and the compaction flow; the failure paths must leave the session file byte-for-byte unchanged.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class SessionCompactionTests : IDisposable
{
	private static readonly DateTimeOffset T0 = new(2026, 11, 12, 10, 0, 0, TimeSpan.Zero);

	private readonly string _tempDir;
	private readonly string _workDir;
	private readonly AppSettingsService _settings;
	private readonly SessionFileService _files;
	private readonly CompactionProcessManagerFake _process = new();
	private readonly FakeRegistryForTurnService _registry = new();
	private readonly CompactionTurnServiceFake _turns = new();
	private readonly SessionCompactionService _sut;

	public SessionCompactionTests()
	{
		_tempDir = Path.Combine(Path.GetTempPath(), "cm_test_compact_" + Path.GetRandomFileName());
		_workDir = Path.Combine(_tempDir, "project");
		Directory.CreateDirectory(_workDir);
		_settings = new AppSettingsService(_tempDir);
		_settings.Settings.SessionFilesRoot = _tempDir;
		_files = new SessionFileService(_settings);
		_sut = new SessionCompactionService(_process, _files, _settings, _registry, _turns);
	}

	public void Dispose()
	{
		foreach (var file in Directory.EnumerateFiles(_tempDir, "*", SearchOption.AllDirectories))
			File.SetAttributes(file, FileAttributes.Normal);
		Directory.Delete(_tempDir, recursive: true);
	}

	private static SessionEntryModel Entry(int minute, string role, string content) =>
		new() { Timestamp = T0.AddMinutes(minute), Role = role, Content = content };

	private SessionNodeModel NodeWithHistory(string? claudeSessionId = "claude-1")
	{
		var fileName = _files.CreateSessionFile();
		_files.AppendMessage(fileName, Constants.SessionFile.RoleUser, "first question");
		_files.AppendMessage(fileName, Constants.SessionFile.RoleAssistant, "first answer");
		_files.AppendMessage(fileName, Constants.SessionFile.RoleSystem, "[Interrupted by user]");
		return new SessionNodeModel
		{
			NodeId = Guid.NewGuid().ToString("N"), Name = "S", FileName = fileName,
			WorkingDirectory = _workDir, ClaudeSessionId = claudeSessionId,
		};
	}

	private static SessionCompactionOptionsModel Options(string? model = null, string? effort = null, string? profileDir = null, ClaudeModelInfo? info = null) =>
		new(model, effort, profileDir, info);

	private byte[] FileBytes(SessionNodeModel node) => File.ReadAllBytes(_files.GetFullPath(node.FileName));

	private void Answers(string text) =>
		_process.Behaviour = (onEvent, _) =>
		{
			onEvent(new ClaudeStreamEvent { Type = "assistant", Content = text });
			onEvent(new ClaudeStreamEvent { Type = "result", SessionId = "compaction-session" });
			return Task.CompletedTask;
		};

	// ── Stop (V1): a cancelled compaction never touches the transcript ──────────────────────

	[Fact]
	public async Task StopDuringTheCliCall_LeavesTheFileUnchanged_AndTheSessionAttached()
	{
		var node = NodeWithHistory();
		var before = FileBytes(node);
		using var cts = new CancellationTokenSource();
		_process.Behaviour = async (onEvent, ct) =>
		{
			onEvent(new ClaudeStreamEvent { Type = "assistant", Content = "half of a summary" });
			cts.Cancel();
			await Task.Delay(Timeout.Infinite, ct);
		};

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _sut.CompactAsync(node, Options(), cts.Token));

		Assert.Equal(before, FileBytes(node));
		Assert.Equal("claude-1", node.ClaudeSessionId);
		Assert.Empty(node.PriorClaudeSessionIds);
	}

	[Fact]
	public async Task StopAfterTheCliAnswered_ButBeforeTheRewrite_LeavesTheFileUnchanged()
	{
		var node = NodeWithHistory();
		var before = FileBytes(node);
		using var cts = new CancellationTokenSource();
		_process.Behaviour = (onEvent, _) =>
		{
			onEvent(new ClaudeStreamEvent { Type = "assistant", Content = "the whole summary" });
			cts.Cancel(); // the process manager returned normally, the token was cancelled meanwhile
			return Task.CompletedTask;
		};

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _sut.CompactWhileHoldingTurnLockAsync(node, Options(), cts.Token));

		Assert.Equal(before, FileBytes(node));
		Assert.Equal("claude-1", node.ClaudeSessionId);
	}

	// ── Prompt building ──────────────────────────────────────────────────────────

	[Fact]
	public void Transcript_TakesUserAndAssistantEntriesAfterTheLastCompaction()
	{
		var entries = new[]
		{
			Entry(0, Constants.SessionFile.RoleUser, "old question"),
			Entry(1, Constants.SessionFile.RoleCompaction, ""),
			Entry(2, Constants.SessionFile.RoleUser, "middle question"),
			Entry(3, Constants.SessionFile.RoleCompaction, ""),
			Entry(4, Constants.SessionFile.RoleUser, "new question"),
			Entry(5, Constants.SessionFile.RoleAssistant, "new answer"),
		};

		var transcript = CompactionPromptBuilder.BuildTranscript(entries);

		Assert.DoesNotContain("old question", transcript);
		Assert.DoesNotContain("middle question", transcript);
		Assert.Contains("[2026-11-12T10:04:00Z] USER", transcript);
		Assert.Contains("new question", transcript);
		Assert.Contains("new answer", transcript);
	}

	[Fact]
	public void Transcript_WithoutCompaction_UsesTheWholeFile_AndSkipsSystemAndEmptyEntries()
	{
		var entries = new[]
		{
			Entry(0, Constants.SessionFile.RoleUser, "question"),
			Entry(1, Constants.SessionFile.RoleSystem, "[Interrupted by user]"),
			Entry(2, Constants.SessionFile.RoleAssistant, "   "),
			Entry(3, Constants.SessionFile.RoleAssistant, "answer"),
		};

		var transcript = CompactionPromptBuilder.BuildTranscript(entries);

		Assert.Contains("question", transcript);
		Assert.Contains("answer", transcript);
		Assert.DoesNotContain("Interrupted", transcript);
		Assert.DoesNotContain("SYSTEM", transcript);
		Assert.Equal(2, transcript.Split('\n').Count(l => l.Contains(" USER") || l.Contains(" ASSISTANT")));
	}

	[Fact]
	public void Prompt_IsNull_WhenOnlyCompactionAndSystemEntriesRemain()
	{
		var entries = new[]
		{
			Entry(0, Constants.SessionFile.RoleUser, "old"),
			Entry(1, Constants.SessionFile.RoleCompaction, ""),
			Entry(2, Constants.SessionFile.RoleSystem, "[note]"),
		};

		Assert.Null(CompactionPromptBuilder.BuildPrompt(entries, _workDir));
	}

	[Fact]
	public void Prompt_IncludesTheGlossary_WhenTheProjectHasOne()
	{
		Directory.CreateDirectory(Path.Combine(_workDir, "docs"));
		File.WriteAllText(Path.Combine(_workDir, "docs", "glossary.md"), "Widget: a thing that widgets");

		var prompt = CompactionPromptBuilder.BuildPrompt(new[] { Entry(0, Constants.SessionFile.RoleUser, "hello there") }, _workDir);

		Assert.NotNull(prompt);
		Assert.Contains("PROJECT GLOSSARY:", prompt);
		Assert.Contains("Widget: a thing that widgets", prompt);
		Assert.Contains("hello there", prompt);
	}

	[Fact]
	public void Prompt_HasNoGlossarySection_WhenTheProjectHasNone()
	{
		var prompt = CompactionPromptBuilder.BuildPrompt(new[] { Entry(0, Constants.SessionFile.RoleUser, "hello there") }, _workDir);

		Assert.NotNull(prompt);
		Assert.DoesNotContain("PROJECT GLOSSARY:", prompt);
		Assert.Contains("hello there", prompt);
	}

	// ── Success ──────────────────────────────────────────────────────────────────

	[Fact]
	public async Task Success_RewritesTheFile_RaisesRewritten_AndDetachesTheClaudeSession()
	{
		var node = NodeWithHistory();
		var rewritten = new List<string>();
		_files.SessionFileRewritten += (_, e) => rewritten.Add(e.FileName);
		Answers("[2026-11-12T10:00:00Z] USER\nfirst question\n\n[2026-11-12T10:01:00Z] ASSISTANT\nfirst answer");

		var result = await _sut.CompactAsync(node, Options());

		Assert.Equal(SessionCompactionOutcome.Compacted, result.Outcome);
		Assert.Null(result.Error);
		Assert.True(result.SessionDetached);
		Assert.Equal(Constants.Compaction.CompactedMessage, result.SystemMessage);
		Assert.Contains(node.FileName, rewritten);

		var entries = _files.ReadEntries(node.FileName);
		Assert.Equal(Constants.SessionFile.RoleCompaction, entries[0].Role);
		Assert.Equal(new[] { "first question", "first answer" },
			entries.Where(e => e.Role is Constants.SessionFile.RoleUser or Constants.SessionFile.RoleAssistant).Select(e => e.Content));
		Assert.DoesNotContain(entries, e => e.Role == Constants.SessionFile.RoleSystem);

		Assert.Null(node.ClaudeSessionId);
		Assert.Equal(new[] { "claude-1" }, node.PriorClaudeSessionIds);
		Assert.True(File.Exists(Path.Combine(_tempDir, Constants.SettingsFileName)));
	}

	[Fact]
	public async Task Success_WithoutLiveClaudeSession_ReportsNothingDetached()
	{
		var node = NodeWithHistory(claudeSessionId: null);
		Answers("[2026-11-12T10:00:00Z] USER\nfirst question");

		var result = await _sut.CompactAsync(node, Options());

		Assert.Equal(SessionCompactionOutcome.Compacted, result.Outcome);
		Assert.False(result.SessionDetached);
		Assert.Empty(node.PriorClaudeSessionIds);
	}

	[Fact]
	public async Task Call_IsStateless_AndCarriesTheSelectedModelEffortAndProfile()
	{
		var node = NodeWithHistory();
		Answers("compacted");

		await _sut.CompactAsync(node, Options("claude-opus-x", "high", @"C:\profiles\p1"));

		var call = Assert.Single(_process.Calls);
		Assert.Null(call.SessionId);
		Assert.Null(call.McpConfigPath);
		Assert.Equal(_workDir, call.WorkingDirectory);
		Assert.Equal("claude-opus-x", call.Model);
		Assert.Equal("high", call.Effort);
		Assert.Equal(@"C:\profiles\p1", call.ProfileConfigDir);
		Assert.Contains("first question", call.Prompt);
		Assert.DoesNotContain("Interrupted", call.Prompt);
		Assert.Null(call.OllamaBaseUrl);
		Assert.False(call.DisableTools);
		Assert.False(call.StrictMcpConfig);
	}

	[Fact]
	public async Task Call_ForAToolCapableOllamaModel_UsesTheLocalToolProfile()
	{
		_settings.Settings.OllamaBaseUrl = "http://ollama.local:11434";
		var node = NodeWithHistory();
		Answers("compacted");
		var model = new ClaudeModelInfo("gemma4:26b", "", "Gemma", ModelProvider.Ollama, SupportsTools: true);

		await _sut.CompactAsync(node, Options("gemma4:26b", info: model));

		var call = Assert.Single(_process.Calls);
		Assert.Equal("http://ollama.local:11434", call.OllamaBaseUrl);
		Assert.False(call.DisableTools);
		Assert.True(call.StrictMcpConfig);
		Assert.NotNull(call.LocalToolSpec);
	}

	[Fact]
	public async Task Call_ForAModelWithoutToolSupport_DisablesTools()
	{
		var node = NodeWithHistory();
		Answers("compacted");
		var model = new ClaudeModelInfo("tiny:1b", "", "Tiny", ModelProvider.Ollama, SupportsTools: false);

		await _sut.CompactAsync(node, Options("tiny:1b", info: model));

		var call = Assert.Single(_process.Calls);
		Assert.True(call.DisableTools);
		Assert.False(call.StrictMcpConfig);
	}

	// ── Failure paths: the file must stay untouched ──────────────────────────────

	[Fact]
	public async Task NothingToCompact_SendsNothing_AndLeavesTheFileUntouched()
	{
		var fileName = _files.CreateSessionFile();
		_files.AppendMessage(fileName, Constants.SessionFile.RoleUser, "old");
		_files.AppendMessage(fileName, Constants.SessionFile.RoleAssistant, "old answer");
		_files.AppendCompactionSeparator(fileName);
		var node = new SessionNodeModel { NodeId = "n", Name = "S", FileName = fileName, WorkingDirectory = _workDir, ClaudeSessionId = "claude-1" };
		var before = FileBytes(node);

		var result = await _sut.CompactAsync(node, Options());

		Assert.Equal(SessionCompactionOutcome.NothingToCompact, result.Outcome);
		Assert.Equal(Constants.Compaction.NothingToCompactMessage, result.SystemMessage);
		Assert.Empty(_process.Calls);
		Assert.Equal(before, FileBytes(node));
		Assert.Equal("claude-1", node.ClaudeSessionId);
	}

	[Fact]
	public async Task ErrorEvent_LeavesTheFileUntouched_EvenWhenAssistantTextWasCaptured()
	{
		var node = NodeWithHistory();
		var before = FileBytes(node);
		_process.Behaviour = (onEvent, _) =>
		{
			onEvent(new ClaudeStreamEvent { Type = "assistant", Content = "partial compacted text" });
			onEvent(new ClaudeStreamEvent { Type = "result", IsError = true, Content = "usage limit reached" });
			return Task.CompletedTask;
		};

		var result = await _sut.CompactAsync(node, Options());

		Assert.Equal(SessionCompactionOutcome.Failed, result.Outcome);
		Assert.Equal("usage limit reached", result.Error);
		Assert.Equal("[Compaction failed — session left unchanged: usage limit reached]", result.SystemMessage);
		Assert.False(result.SessionDetached);
		Assert.Equal(before, FileBytes(node));
		Assert.Equal("claude-1", node.ClaudeSessionId);
		Assert.Empty(node.PriorClaudeSessionIds);
	}

	[Fact]
	public async Task FirstErrorWins_WhenSeveralErrorEventsArrive()
	{
		var node = NodeWithHistory();
		_process.Behaviour = (onEvent, _) =>
		{
			onEvent(new ClaudeStreamEvent { Type = "result", IsError = true, Content = "first" });
			onEvent(new ClaudeStreamEvent { Type = "result", IsError = true, Content = "second" });
			return Task.CompletedTask;
		};

		var result = await _sut.CompactAsync(node, Options());

		Assert.Equal("first", result.Error);
	}

	[Theory]
	[InlineData("")]
	[InlineData("   \n  ")]
	public async Task EmptyAnswer_LeavesTheFileUntouched(string answer)
	{
		var node = NodeWithHistory();
		var before = FileBytes(node);
		Answers(answer);

		var result = await _sut.CompactAsync(node, Options());

		Assert.Equal(SessionCompactionOutcome.Failed, result.Outcome);
		Assert.Null(result.Error);
		Assert.Equal(Constants.Compaction.EmptyResultMessage, result.SystemMessage);
		Assert.Equal(before, FileBytes(node));
		Assert.Equal("claude-1", node.ClaudeSessionId);
	}

	[Fact]
	public async Task NoEventsAtAll_LeavesTheFileUntouched()
	{
		var node = NodeWithHistory();
		var before = FileBytes(node);

		var result = await _sut.CompactAsync(node, Options());

		Assert.Equal(SessionCompactionOutcome.Failed, result.Outcome);
		Assert.Equal(before, FileBytes(node));
	}

	[Fact]
	public async Task ProcessManagerThrowing_IsAFailure_AndLeavesTheFileUntouched()
	{
		var node = NodeWithHistory();
		var before = FileBytes(node);
		_process.Behaviour = (_, _) => throw new InvalidOperationException("claude.exe not found");

		var result = await _sut.CompactAsync(node, Options());

		Assert.Equal(SessionCompactionOutcome.Failed, result.Outcome);
		Assert.Equal("claude.exe not found", result.Error);
		Assert.Equal(before, FileBytes(node));
		Assert.Equal("claude-1", node.ClaudeSessionId);
	}

	[Fact]
	public async Task RewriteFailing_LeavesTheOriginalFile_AndTheClaudeSessionAttached()
	{
		var node = NodeWithHistory();
		var before = FileBytes(node);
		File.SetAttributes(_files.GetFullPath(node.FileName), FileAttributes.ReadOnly); // the final move cannot replace it
		Answers("[2026-11-12T10:00:00Z] USER\ncompacted");

		var result = await _sut.CompactAsync(node, Options());

		Assert.Equal(SessionCompactionOutcome.Failed, result.Outcome);
		Assert.NotNull(result.Error);
		Assert.False(result.SessionDetached);
		Assert.Equal(before, FileBytes(node));
		Assert.Equal("claude-1", node.ClaudeSessionId);
		Assert.Empty(node.PriorClaudeSessionIds);
	}

	[Fact]
	public async Task Cancellation_Propagates_LeavesTheFileUntouched_AndReleasesLockAndRunningFlag()
	{
		var node = NodeWithHistory();
		var before = FileBytes(node);
		using var cts = new CancellationTokenSource();
		_process.Behaviour = async (onEvent, ct) =>
		{
			onEvent(new ClaudeStreamEvent { Type = "assistant", Content = "partial" });
			cts.Cancel();
			await Task.Delay(Timeout.Infinite, ct);
		};

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _sut.CompactAsync(node, Options(), cts.Token));

		Assert.Equal(before, FileBytes(node));
		Assert.Equal("claude-1", node.ClaudeSessionId);
		Assert.Equal(1, _turns.GetTurnLock(node.NodeId).CurrentCount);
		Assert.Equal(new[] { true, false }, _registry.SetRunningCalls.Select(c => c.isRunning));
	}

	// ── Lock and running flag ────────────────────────────────────────────────────

	[Fact]
	public async Task RunningFlag_IsSetWhileRunning_AndClearedAfterSuccessAndAfterFailure()
	{
		var node = NodeWithHistory();
		var duringRun = new List<int>();
		_process.Behaviour = (onEvent, _) =>
		{
			duringRun.Add(_registry.SetRunningCalls.Count);
			onEvent(new ClaudeStreamEvent { Type = "assistant", Content = "compacted" });
			return Task.CompletedTask;
		};

		await _sut.CompactAsync(node, Options());
		Assert.Equal(new[] { 1 }, duringRun);
		Assert.Equal(new[] { true, false }, _registry.SetRunningCalls.Select(c => c.isRunning));
		Assert.All(_registry.SetRunningCalls, c => Assert.Equal(node.NodeId, c.nodeId));

		_registry.SetRunningCalls.Clear();
		_process.Behaviour = (onEvent, _) =>
		{
			onEvent(new ClaudeStreamEvent { Type = "result", IsError = true, Content = "boom" });
			return Task.CompletedTask;
		};
		await _sut.CompactAsync(node, Options());
		Assert.Equal(new[] { true, false }, _registry.SetRunningCalls.Select(c => c.isRunning));
	}

	[Fact]
	public async Task CompactAsync_WaitsForTheTurnLock_AndReleasesItAfterwards()
	{
		var node = NodeWithHistory();
		Answers("compacted");
		var turnLock = _turns.GetTurnLock(node.NodeId);
		await turnLock.WaitAsync();

		var run = _sut.CompactAsync(node, Options());
		await Task.Delay(150);
		Assert.False(run.IsCompleted);
		Assert.Empty(_process.Calls);

		turnLock.Release();
		var result = await run.WaitAsync(TimeSpan.FromSeconds(10));

		Assert.Equal(SessionCompactionOutcome.Compacted, result.Outcome);
		Assert.Equal(1, turnLock.CurrentCount);
	}

	[Fact]
	public async Task CompactWhileHoldingTurnLock_DoesNotDeadlock_AndKeepsTheCallersLock()
	{
		var node = NodeWithHistory();
		Answers("compacted");
		var turnLock = _turns.GetTurnLock(node.NodeId);
		await turnLock.WaitAsync();

		var result = await _sut.CompactWhileHoldingTurnLockAsync(node, Options()).WaitAsync(TimeSpan.FromSeconds(10));

		Assert.Equal(SessionCompactionOutcome.Compacted, result.Outcome);
		Assert.Equal(0, turnLock.CurrentCount); // still held by the caller
		turnLock.Release();
	}

	[Fact]
	public async Task TwoCompactions_OfTheSameSession_DoNotOverlap()
	{
		var node = NodeWithHistory();
		var concurrent = 0;
		var maxConcurrent = 0;
		_process.Behaviour = async (onEvent, _) =>
		{
			var now = Interlocked.Increment(ref concurrent);
			maxConcurrent = Math.Max(maxConcurrent, now);
			await Task.Delay(100);
			onEvent(new ClaudeStreamEvent { Type = "assistant", Content = "[2026-11-12T10:00:00Z] USER\ncompacted" });
			Interlocked.Decrement(ref concurrent);
		};

		var first = _sut.CompactAsync(node, Options());
		var second = _sut.CompactAsync(node, Options());
		await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(20));

		Assert.Equal(1, maxConcurrent);
	}
}
