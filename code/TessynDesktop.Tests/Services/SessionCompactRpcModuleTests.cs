using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary>Plays back a configured compaction result and records the options it was called with.</summary>
internal sealed class CompactionServiceFake : ISessionCompactionService
{
	private readonly TaskCompletionSource<bool> _called = new(TaskCreationOptions.RunContinuationsAsynchronously);

	public SessionCompactionResultModel Result { get; set; } =
		new(SessionCompactionOutcome.Compacted, null, Constants.Compaction.CompactedMessage, true);

	public Exception? Throw { get; set; }
	public Task? Gate { get; set; }
	public SessionCompactionOptionsModel? Options { get; private set; }
	public SessionNodeModel? Node { get; private set; }
	public Task Called => _called.Task;

	public async Task<SessionCompactionResultModel> CompactAsync(SessionNodeModel node, SessionCompactionOptionsModel options, CancellationToken cancellationToken = default)
	{
		Node = node;
		Options = options;
		_called.TrySetResult(true);
		if (Gate != null)
			await Gate;
		if (Throw != null)
			throw Throw;
		return Result;
	}

	public Task<SessionCompactionResultModel> CompactWhileHoldingTurnLockAsync(SessionNodeModel node, SessionCompactionOptionsModel options, CancellationToken cancellationToken = default) =>
		throw new NotSupportedException();
}

internal sealed class CompactionModelServiceFake : IClaudeModelService
{
	public List<ClaudeModelInfo> Models { get; } = new();
	public IReadOnlyList<ClaudeModelInfo> GetCachedModels() => Models;
	public Task EnsureModelsLoadedAsync() => Task.CompletedTask;
	public event EventHandler? ModelsUpdated { add { } remove { } }
}

internal sealed class CompactionProfileServiceFake : IClaudeProfileService
{
	public string ProfilesRootDirectory => "profiles";
	public Task<string?> GetAccountEmailAsync(string claudePath, string? configDir) => Task.FromResult<string?>(null);
	public Task LaunchAuthLoginAsync(string claudePath, string? configDir) => Task.CompletedTask;
	public string? GetConfigDirForProfile(int profileIndex, IReadOnlyList<ClaudeProfileModel> profiles) => $"cfg{profileIndex}";
	public void MarkProfileDirectoryRemoved(string profileId) { }
}

internal sealed class CompactionLabelServiceFake : IProfileLabelService
{
	public Task<string> GetDefaultLabelAsync() => Task.FromResult("[Sys] Default");
	public string GetDefaultLabelCached() => "[Sys] Default";
}

/// <summary>The <c>session.compact</c> remote method: validation, option resolution, background run and outcome reporting.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class SessionCompactRpcModuleTests : IDisposable
{
	private readonly string _tempDir;
	private readonly AppSettingsService _settings;
	private readonly SessionFileService _files;
	private readonly SessionRegistry _registry;
	private readonly CompactionServiceFake _compaction = new();
	private readonly CompactionTurnServiceFake _turns = new();
	private readonly CompactionModelServiceFake _models = new();
	private readonly TdcpDispatcher _dispatcher;
	private readonly SessionNodeModel _node;
	private readonly List<SessionTurnCompletedEventArgs> _completed = new();

	public SessionCompactRpcModuleTests()
	{
		_tempDir = Path.Combine(Path.GetTempPath(), "cm_test_compact_rpc_" + Path.GetRandomFileName());
		Directory.CreateDirectory(_tempDir);
		_settings = new AppSettingsService(_tempDir);
		_settings.Settings.SessionFilesRoot = _tempDir;
		_files = new SessionFileService(_settings);
		_registry = new SessionRegistry(_settings, _files, new FakeDirectoryLabelService(), new FakeClaudeSessionStatusService());

		var dirPath = Path.Combine(_tempDir, "project");
		Directory.CreateDirectory(dirPath);
		_node = new SessionNodeModel
		{
			NodeId = Guid.NewGuid().ToString("N"), Name = "S", FileName = _files.CreateSessionFile(), WorkingDirectory = dirPath,
		};
		_settings.Settings.Tree.Add(new DirectoryNodeModel { Path = dirPath, Sessions = { _node } });
		_registry.LoadFromSettings();

		_files.TurnCompleted += (_, e) => { lock (_completed) _completed.Add(e); };
		var module = new SessionCompactRpcModule(_registry, _compaction, _turns, _files, _settings, _models, new CompactionProfileServiceFake(),
			new TurnOptionsResolver(_models, _settings, new CompactionLabelServiceFake()));
		_dispatcher = new TdcpDispatcher([module]);
	}

	public void Dispose() => Directory.Delete(_tempDir, recursive: true);

	private async Task<JsonNode> Call(JsonNode? @params)
	{
		var request = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = 1, ["method"] = Constants.Remote.Methods.SessionCompact };
		if (@params != null)
			request["params"] = @params;
		return await _dispatcher.HandleRequestAsync(request, TdcpTransport.Relay, CancellationToken.None);
	}

	private Task<JsonNode> Call(string nodeId, params (string key, JsonNode? value)[] extra)
	{
		var p = new JsonObject { ["nodeId"] = nodeId };
		foreach (var (key, value) in extra)
			p[key] = value;
		return Call(p);
	}

	private static int ErrorCode(JsonNode response) => response["error"]!["code"]!.GetValue<int>();

	private async Task WaitForCompletedNotification()
	{
		var deadline = DateTime.UtcNow.AddSeconds(10);
		while (DateTime.UtcNow < deadline)
		{
			lock (_completed)
				if (_completed.Count > 0) return;
			await Task.Delay(10);
		}

		throw new TimeoutException("turn.completed was not raised");
	}

	private async Task RerunAndWait()
	{
		lock (_completed) _completed.Clear();
		await Call(_node.NodeId);
		await WaitForCompletedNotification();
	}

	// ── Registration and validation ──────────────────────────────────────────────

	[Fact]
	public void IsRegistered_AndRemoteCallable()
	{
		Assert.True(_dispatcher.IsRemoteCallable(Constants.Remote.Methods.SessionCompact));
	}

	[Fact]
	public async Task MissingNodeId_IsInvalidParams()
	{
		var response = await Call(new JsonObject());
		Assert.Equal(-32602, ErrorCode(response));
	}

	[Fact]
	public async Task NodeIdOfTheWrongType_IsInvalidParams()
	{
		var response = await Call(new JsonObject { ["nodeId"] = 5 });
		Assert.Equal(-32602, ErrorCode(response));
	}

	[Fact]
	public async Task UnknownEffort_IsInvalidParams_AndNothingRuns()
	{
		var response = await Call(_node.NodeId, ("effort", "extreme"));

		Assert.Equal(-32602, ErrorCode(response));
		Assert.Null(_compaction.Options);
	}

	[Fact]
	public async Task NonStringModelId_IsInvalidParams()
	{
		var response = await Call(_node.NodeId, ("modelId", 7));
		Assert.Equal(-32602, ErrorCode(response));
	}

	[Fact]
	public async Task UnknownModelId_IsInvalidParams_LikeSessionSend()
	{
		var response = await Call(_node.NodeId, ("modelId", "no-such-model"));

		Assert.Equal(-32602, ErrorCode(response));
		Assert.Null(_compaction.Options);
	}

	[Fact]
	public async Task UnknownProfileId_IsInvalidParams()
	{
		var response = await Call(_node.NodeId, ("profileId", "profile_99"));

		Assert.Equal(-32602, ErrorCode(response));
		Assert.Null(_compaction.Options);
	}

	[Fact]
	public async Task UnknownSession_IsADaemonError()
	{
		var response = await Call("0123456789abcdef0123456789abcdef");

		Assert.Equal(-32000, ErrorCode(response));
		Assert.Equal("Session not found", response["error"]!["message"]!.GetValue<string>());
		Assert.Null(_compaction.Options);
	}

	// ── Accepting ────────────────────────────────────────────────────────────────

	[Fact]
	public async Task Accepted_ReturnsImmediately_WhileTheRunContinuesInTheBackground()
	{
		var gate = new TaskCompletionSource();
		_compaction.Gate = gate.Task;

		var response = await Call(_node.NodeId);

		Assert.True(response["result"]!["accepted"]!.GetValue<bool>());
		Assert.False(response["result"]!["queued"]!.GetValue<bool>());
		await _compaction.Called.WaitAsync(TimeSpan.FromSeconds(10));
		Assert.Same(_node, _compaction.Node);
		lock (_completed)
			Assert.Empty(_completed); // still running

		gate.SetResult();
		await WaitForCompletedNotification();
	}

	[Fact]
	public async Task Queued_IsTrue_WhenTheSessionIsRunningAtAcceptTime()
	{
		_registry.SetSessionRunning(_node.NodeId, true);

		var response = await Call(_node.NodeId);

		Assert.True(response["result"]!["queued"]!.GetValue<bool>());
		await WaitForCompletedNotification();
		_registry.SetSessionRunning(_node.NodeId, false);
	}

	// ── Option resolution ────────────────────────────────────────────────────────

	[Fact]
	public async Task NoOptions_ResolvesTheModelLikeAHeadlessTurn_NodeThenDirectoryThenApp()
	{
		_settings.Settings.SelectedModelId = "app-model";
		await Call(_node.NodeId);
		await WaitForCompletedNotification();
		Assert.Equal("app-model", _compaction.Options!.ModelId);

		_settings.Settings.Tree[0].SelectedModelId = "dir-model";
		await RerunAndWait();
		Assert.Equal("dir-model", _compaction.Options!.ModelId);

		_node.ModelId = "node-model";
		await RerunAndWait();
		Assert.Equal("node-model", _compaction.Options!.ModelId);
		Assert.Null(_compaction.Options.Effort);
		Assert.Null(_compaction.Options.ProfileConfigDir);
	}

	[Fact]
	public async Task ExplicitOptions_AreForwarded_AndTheModelInfoComesFromTheCatalog()
	{
		var info = new ClaudeModelInfo("claude-opus-x", "opus", "Opus X");
		_models.Models.Add(info);
		_settings.Settings.Profiles.Add(new ClaudeProfileModel { ProfileId = "profile_1", DisplayName = "one@x" });
		_settings.Settings.Profiles.Add(new ClaudeProfileModel { ProfileId = "profile_2", DisplayName = "two@x" });

		await Call(_node.NodeId, ("modelId", "claude-opus-x"), ("effort", "high"), ("profileId", "profile_2"));
		await WaitForCompletedNotification();

		Assert.Equal("claude-opus-x", _compaction.Options!.ModelId);
		Assert.Equal("high", _compaction.Options.Effort);
		Assert.Equal("cfg2", _compaction.Options.ProfileConfigDir);
		Assert.Same(info, _compaction.Options.ModelInfo);
	}

	[Fact]
	public async Task DefaultValues_MeanNoFlags()
	{
		_settings.Settings.SelectedModelId = "app-model";
		_settings.Settings.Profiles.Add(new ClaudeProfileModel { ProfileId = "profile_1", DisplayName = "one@x" });
		_turns.ProfileName = "one@x"; // the session's own profile would otherwise be used

		await Call(_node.NodeId, ("modelId", "default"), ("effort", "default"), ("profileId", "default"));
		await WaitForCompletedNotification();

		Assert.Null(_compaction.Options!.ModelId);
		Assert.Null(_compaction.Options.Effort);
		Assert.Null(_compaction.Options.ProfileConfigDir);
		Assert.Null(_compaction.Options.ModelInfo);
	}

	[Fact]
	public async Task NoProfileId_UsesTheSessionsOwnProfile()
	{
		_settings.Settings.Profiles.Add(new ClaudeProfileModel { ProfileId = "profile_1", DisplayName = "one@x" });
		_settings.Settings.Profiles.Add(new ClaudeProfileModel { ProfileId = "profile_2", DisplayName = "Two@x" });
		_turns.ProfileName = "two@x";

		await Call(_node.NodeId);
		await WaitForCompletedNotification();

		Assert.Equal("cfg2", _compaction.Options!.ProfileConfigDir);
	}

	[Fact]
	public async Task NoProfileId_WithADeletedProfile_FallsBackToDefault()
	{
		_turns.ProfileName = "gone@x";

		await Call(_node.NodeId);
		await WaitForCompletedNotification();

		Assert.Null(_compaction.Options!.ProfileConfigDir);
	}

	// ── Outcomes ─────────────────────────────────────────────────────────────────

	[Fact]
	public async Task Success_NotifiesTurnCompletedWithoutError_AndAppendsNothing()
	{
		var before = File.ReadAllBytes(_files.GetFullPath(_node.FileName));

		await Call(_node.NodeId);
		await WaitForCompletedNotification();

		var completed = Assert.Single(_completed);
		Assert.Equal(_node.FileName, completed.FileName);
		Assert.False(completed.Cancelled);
		Assert.Null(completed.ErrorText);
		Assert.Equal(before, File.ReadAllBytes(_files.GetFullPath(_node.FileName)));
	}

	[Fact]
	public async Task Failure_AppendsTheSystemLine_AndReportsTheError()
	{
		_files.AppendMessage(_node.FileName, Constants.SessionFile.RoleUser, "question");
		_compaction.Result = new SessionCompactionResultModel(SessionCompactionOutcome.Failed, "usage limit",
			"[Compaction failed — session left unchanged: usage limit]", false);

		await Call(_node.NodeId);
		await WaitForCompletedNotification();

		Assert.Equal("usage limit", Assert.Single(_completed).ErrorText);
		var entries = _files.ReadEntries(_node.FileName);
		Assert.Equal(Constants.SessionFile.RoleUser, entries[0].Role);
		Assert.Equal("question", entries[0].Content);
		Assert.Equal(Constants.SessionFile.RoleSystem, entries[1].Role);
		Assert.Equal("[Compaction failed — session left unchanged: usage limit]", entries[1].Content);
	}

	[Fact]
	public async Task NothingToCompact_AppendsTheSystemLine_WithoutAnError()
	{
		_compaction.Result = new SessionCompactionResultModel(SessionCompactionOutcome.NothingToCompact, null,
			Constants.Compaction.NothingToCompactMessage, false);

		await Call(_node.NodeId);
		await WaitForCompletedNotification();

		Assert.Null(Assert.Single(_completed).ErrorText);
		var entry = Assert.Single(_files.ReadEntries(_node.FileName));
		Assert.Equal(Constants.Compaction.NothingToCompactMessage, entry.Content);
	}

	[Fact]
	public async Task EmptyResult_ReportsItsMessageAsTheError()
	{
		_compaction.Result = new SessionCompactionResultModel(SessionCompactionOutcome.Failed, null,
			Constants.Compaction.EmptyResultMessage, false);

		await Call(_node.NodeId);
		await WaitForCompletedNotification();

		Assert.Equal(Constants.Compaction.EmptyResultMessage, Assert.Single(_completed).ErrorText);
	}

	[Fact]
	public async Task ServiceThrowing_IsReportedAsAnError_AndDoesNotCrashTheProcess()
	{
		_compaction.Throw = new InvalidOperationException("disk gone");

		var response = await Call(_node.NodeId);
		await WaitForCompletedNotification();

		Assert.True(response["result"]!["accepted"]!.GetValue<bool>());
		Assert.Equal("disk gone", Assert.Single(_completed).ErrorText);
	}

	// ── FR.12.35 — the session's model and effort (TC.ME.02) ─────────────────────

	[Fact]
	public async Task NoOptions_UsesTheSessionsOwnModelAndEffort()
	{
		_settings.Settings.SelectedModelId = "app-model";
		_node.ModelId = "node-model";
		_node.Effort  = "max";

		await Call(_node.NodeId);
		await WaitForCompletedNotification();

		Assert.Equal("node-model", _compaction.Options!.ModelId);
		Assert.Equal("max", _compaction.Options.Effort);
	}

	[Fact]
	public async Task ExplicitOptions_AreNotKeptOnTheSession()
	{
		_models.Models.Add(new ClaudeModelInfo("claude-opus-x", "opus", "Opus X"));

		await Call(_node.NodeId, ("modelId", "claude-opus-x"), ("effort", "high"));
		await WaitForCompletedNotification();

		Assert.Equal("high", _compaction.Options!.Effort);
		Assert.Null(_node.ModelId);
		Assert.Null(_node.Effort);
	}
}
