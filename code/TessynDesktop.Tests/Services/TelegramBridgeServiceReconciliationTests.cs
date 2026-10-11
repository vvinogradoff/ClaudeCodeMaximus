using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Serilog.Events;
using TessynDesktop.Models;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary>Wraps the real settings service and counts <see cref="Save"/> calls.</summary>
internal sealed class SaveCountingAppSettings : IAppSettingsService
{
	private readonly IAppSettingsService _inner;
	private int _saves;

	public SaveCountingAppSettings(IAppSettingsService inner) => _inner = inner;

	public int SaveCount => Volatile.Read(ref _saves);
	public AppSettingsModel Settings => _inner.Settings;
	public object SyncRoot => _inner.SyncRoot;
	public void Load() => _inner.Load();
	public void Save()
	{
		Interlocked.Increment(ref _saves);
		_inner.Save();
	}
}

/// <summary>Hand-written Serilog sink that keeps every logged event.</summary>
internal sealed class CapturingLogSink : Serilog.Core.ILogEventSink
{
	private readonly List<LogEvent> _events = new();

	public void Emit(LogEvent logEvent)
	{
		lock (_events) _events.Add(logEvent);
	}

	public IReadOnlyList<LogEvent> AtLeast(LogEventLevel level)
	{
		lock (_events) return _events.Where(e => e.Level >= level).ToList();
	}
}

/// <summary>Topic name reconciliation (FR.21.18, FR.21.2). Test cases: TC.21.10, TC.DL.01.</summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class TelegramBridgeServiceTests
{
	private readonly List<TimeSpan> _delays = new();
	private readonly CapturingLogSink _logSink = new();

	private TimeSpan[] RecordedDelays()
	{
		lock (_delays) return _delays.ToArray();
	}

	/// <summary>Builds a bridge whose reconciliation waits are recorded and return immediately (unless the stop token is cancelled).</summary>
	private TelegramBridgeService BuildReconcilingService(IAppSettingsService? settings = null, IDirectoryLabelService? labels = null)
	{
		_service = new TelegramBridgeService(settings ?? _appSettings, _client, _daemon, labels ?? _labelService,
			new TelegramInbox(_client, System.IO.Path.Combine(_tempDir, "inbox")))
		{
			Logger = new Serilog.LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(_logSink).CreateLogger(),
			DelayAsync = (delay, ct) =>
			{
				lock (_delays) _delays.Add(delay);
				ct.ThrowIfCancellationRequested();
				return Task.CompletedTask;
			},
		};
		return _service;
	}

	private static Task BlockUntilCancelled(TimeSpan _, CancellationToken ct) => Task.Delay(Timeout.Infinite, ct);

	private IReadOnlyList<LogEvent> Warnings() => _logSink.AtLeast(LogEventLevel.Warning);

	private void MapTopic(string nodeId, int threadId, string? appliedName = null, bool withSession = true)
	{
		if (withSession)
			_daemon.AddNode(MakeNode(nodeId, "Fix login"));
		_appSettings.Settings.TelegramTopicThreadIdsByNodeId[nodeId] = threadId;
		if (appliedName != null)
			_appSettings.Settings.TelegramTopicNamesByNodeId[nodeId] = appliedName;
	}

	private static TelegramApiException ApiError(int code, string description, int? retryAfter = null) =>
		new(code, description, retryAfter);

	[Fact]
	public async Task Reconcile_NameDiffersFromStored_RenamesOnceAndRecords()
	{
		MapTopic("node-1", 42, "C:\\proj / Fix login");
		var svc = BuildReconcilingService();

		svc.Start();
		await svc.ReconciliationTask;

		Assert.Equal([(-1001L, 42, "TestDir / Fix login")], _client.EditedTopics);
		Assert.Equal("TestDir / Fix login", _appSettings.Settings.TelegramTopicNamesByNodeId["node-1"]);
	}

	[Fact]
	public async Task Reconcile_NoStoredName_RenamesAndRecords()
	{
		MapTopic("node-1", 42);
		var svc = BuildReconcilingService();

		svc.Start();
		await svc.ReconciliationTask;

		Assert.Single(_client.EditedTopics);
		Assert.Equal("TestDir / Fix login", _appSettings.Settings.TelegramTopicNamesByNodeId["node-1"]);
	}

	[Fact]
	public async Task Reconcile_StoredNameEqualsCurrent_MakesNoCallAndNoWait()
	{
		MapTopic("node-1", 42, "TestDir / Fix login");
		var svc = BuildReconcilingService();

		svc.Start();
		await svc.ReconciliationTask;

		Assert.Empty(_client.EditedTopics);
		Assert.Empty(RecordedDelays());
	}

	[Fact]
	public async Task Reconcile_OrphanMapping_SkippedAndRunContinues()
	{
		MapTopic("orphan", 41, "Old / Name", withSession: false);
		MapTopic("node-1", 42);
		var svc = BuildReconcilingService();

		svc.Start();
		await svc.ReconciliationTask;

		Assert.Equal([42], _client.EditedTopics.Select(e => e.threadId));
		Assert.Equal(41, _appSettings.Settings.TelegramTopicThreadIdsByNodeId["orphan"]);
		Assert.Equal("Old / Name", _appSettings.Settings.TelegramTopicNamesByNodeId["orphan"]);
	}

	[Fact]
	public async Task EnsureTopicForSessionAsync_NewTopic_RecordsNameAndReconcileMakesNoCall()
	{
		var svc = BuildReconcilingService();
		var node = MakeNode("node-1", "Fix login");
		_daemon.AddNode(node);

		await svc.EnsureTopicForSessionAsync(node);
		svc.Start();
		await svc.ReconciliationTask;

		Assert.Equal("TestDir / Fix login", _appSettings.Settings.TelegramTopicNamesByNodeId["node-1"]);
		Assert.Empty(_client.EditedTopics);
	}

	[Fact]
	public async Task Reconcile_Renamed_WaitsTopicRenameIntervalAfterRename()
	{
		MapTopic("node-1", 42);
		var svc = BuildReconcilingService();

		svc.Start();
		await svc.ReconciliationTask;

		Assert.Equal(6000, Constants.Telegram.TopicRenameIntervalMs);
		Assert.Equal([TimeSpan.FromMilliseconds(6000)], RecordedDelays());
	}

	[Fact]
	public async Task Reconcile_NotModified_RecordsNameContinuesAndWaitsNoOpInterval()
	{
		MapTopic("node-1", 42);
		MapTopic("node-2", 43);
		_client.EditBehavior = (_, _, _) => Task.FromResult(false);
		var svc = BuildReconcilingService();

		svc.Start();
		await svc.ReconciliationTask;

		Assert.Equal(2, _client.EditedTopics.Count);
		Assert.Equal("TestDir / Fix login", _appSettings.Settings.TelegramTopicNamesByNodeId["node-1"]);
		Assert.Equal("TestDir / Fix login", _appSettings.Settings.TelegramTopicNamesByNodeId["node-2"]);
		Assert.Equal(1000, Constants.Telegram.TopicRenameNoOpIntervalMs);
		Assert.Equal([TimeSpan.FromMilliseconds(1000), TimeSpan.FromMilliseconds(1000)], RecordedDelays());
	}

	[Fact]
	public async Task Reconcile_RateLimitThenSuccess_RetriesSameTopicOnceAndRecords()
	{
		MapTopic("node-1", 42);
		var calls = 0;
		_client.EditBehavior = (_, _, _) => ++calls == 1
			? throw ApiError(429, "Too Many Requests: retry after 3", 3)
			: Task.FromResult(true);
		var svc = BuildReconcilingService();

		svc.Start();
		await svc.ReconciliationTask;

		Assert.Equal([42, 42], _client.EditedTopics.Select(e => e.threadId));
		Assert.Equal("TestDir / Fix login", _appSettings.Settings.TelegramTopicNamesByNodeId["node-1"]);
		Assert.Equal([TimeSpan.FromSeconds(3), TimeSpan.FromMilliseconds(6000)], RecordedDelays());
	}

	[Fact]
	public async Task Reconcile_RateLimitTwice_StopsRunAndDoesNotAttemptLaterTopics()
	{
		MapTopic("node-1", 42);
		MapTopic("node-2", 43);
		_client.EditBehavior = (_, _, _) => throw ApiError(429, "Too Many Requests: retry after 1", 1);
		var svc = BuildReconcilingService();

		svc.Start();
		await svc.ReconciliationTask;

		Assert.Equal([42, 42], _client.EditedTopics.Select(e => e.threadId));
		Assert.Empty(_appSettings.Settings.TelegramTopicNamesByNodeId);
	}

	[Theory]
	[InlineData(0, 1)]
	[InlineData(7, 7)]
	[InlineData(1000, 300)]
	public async Task Reconcile_RateLimit_WaitsRetryAfterClampedToOneThroughThreeHundredSeconds(int retryAfter, int expectedSeconds)
	{
		MapTopic("node-1", 42);
		var calls = 0;
		_client.EditBehavior = (_, _, _) => ++calls == 1
			? throw ApiError(429, "Too Many Requests", retryAfter)
			: Task.FromResult(true);
		var svc = BuildReconcilingService();

		svc.Start();
		await svc.ReconciliationTask;

		Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), RecordedDelays()[0]);
	}

	[Fact]
	public async Task Reconcile_RateLimitWithoutRetryAfter_WaitsOneSecond()
	{
		MapTopic("node-1", 42);
		var calls = 0;
		_client.EditBehavior = (_, _, _) => ++calls == 1
			? throw ApiError(429, "Too Many Requests")
			: Task.FromResult(true);
		var svc = BuildReconcilingService();

		svc.Start();
		await svc.ReconciliationTask;

		Assert.Equal(TimeSpan.FromSeconds(1), RecordedDelays()[0]);
	}

	[Theory]
	[InlineData(403, "Forbidden: bot is not a member")]
	[InlineData(400, "Bad Request: not enough rights to manage topics")]
	public async Task Reconcile_RightsError_StopsRunWithSingleWarningAndNothingRecorded(int code, string description)
	{
		MapTopic("node-1", 42);
		MapTopic("node-2", 43);
		_client.EditBehavior = (_, _, _) => throw ApiError(code, description);
		var svc = BuildReconcilingService();

		svc.Start();
		await svc.ReconciliationTask;

		Assert.Equal([42], _client.EditedTopics.Select(e => e.threadId));
		Assert.Empty(_appSettings.Settings.TelegramTopicNamesByNodeId);
		Assert.Single(Warnings());
	}

	[Fact]
	public async Task Reconcile_ChatNotFound_StopsRunWithSingleWarning()
	{
		MapTopic("node-1", 42);
		MapTopic("node-2", 43);
		_client.EditBehavior = (_, _, _) => throw ApiError(400, "Bad Request: Chat Not Found");
		var svc = BuildReconcilingService();

		svc.Start();
		await svc.ReconciliationTask;

		Assert.Equal([42], _client.EditedTopics.Select(e => e.threadId));
		Assert.Empty(_appSettings.Settings.TelegramTopicNamesByNodeId);
		Assert.Single(Warnings());
	}

	[Fact]
	public async Task Reconcile_OtherApiError_SkipsTopicWaitsNoOpIntervalAndProcessesRest()
	{
		MapTopic("node-1", 42);
		MapTopic("node-2", 43);
		_client.EditBehavior = (thread, _, _) => thread == 42
			? throw ApiError(400, "Bad Request: TOPIC_ID_INVALID")
			: Task.FromResult(true);
		var svc = BuildReconcilingService();

		svc.Start();
		await svc.ReconciliationTask;

		Assert.Equal([42, 43], _client.EditedTopics.Select(e => e.threadId));
		Assert.False(_appSettings.Settings.TelegramTopicNamesByNodeId.ContainsKey("node-1"));
		Assert.True(_appSettings.Settings.TelegramTopicNamesByNodeId.ContainsKey("node-2"));
		Assert.Equal([TimeSpan.FromMilliseconds(1000), TimeSpan.FromMilliseconds(6000)], RecordedDelays());
		Assert.Single(Warnings());
	}

	[Fact]
	public async Task Reconcile_NetworkFailure_StopsRunWithoutThrowing()
	{
		MapTopic("node-1", 42);
		MapTopic("node-2", 43);
		_client.EditBehavior = (_, _, _) => throw new System.Net.Http.HttpRequestException("network down");
		var svc = BuildReconcilingService();

		svc.Start();
		await svc.ReconciliationTask;

		Assert.Equal([42], _client.EditedTopics.Select(e => e.threadId));
		Assert.Empty(_appSettings.Settings.TelegramTopicNamesByNodeId);
		Assert.Single(Warnings());
	}

	[Fact]
	public async Task Reconcile_TimeoutWithoutStopRequest_IsLoggedAsNetworkFailureAndStopsRun()
	{
		MapTopic("node-1", 42);
		MapTopic("node-2", 43);
		_client.EditBehavior = (_, _, _) => throw new TaskCanceledException("HttpClient timeout");
		var svc = BuildReconcilingService();

		svc.Start();
		await svc.ReconciliationTask;

		Assert.Equal([42], _client.EditedTopics.Select(e => e.threadId));
		Assert.Empty(_appSettings.Settings.TelegramTopicNamesByNodeId);
		Assert.Single(Warnings());
	}

	[Fact]
	public async Task Reconcile_ChatIdNotConfigured_DoesNothing()
	{
		MapTopic("node-1", 42);
		_appSettings.Settings.TelegramChatId = 0;
		var svc = BuildReconcilingService();

		svc.Start();
		await svc.ReconciliationTask;

		Assert.Empty(_client.EditedTopics);
		Assert.Empty(_appSettings.Settings.TelegramTopicNamesByNodeId);
	}

	[Fact]
	public async Task Reconcile_FortyFiveTopics_SavesAfter20thAnd40thAndAtEnd()
	{
		for (var i = 0; i < 45; i++)
			MapTopic($"node-{i}", 100 + i);
		var settings = new SaveCountingAppSettings(_appSettings);
		var svc = BuildReconcilingService(settings);

		svc.Start();
		await svc.ReconciliationTask;

		Assert.Equal(45, _appSettings.Settings.TelegramTopicNamesByNodeId.Count);
		Assert.Equal(3, settings.SaveCount);
	}

	[Fact]
	public async Task Reconcile_StoppedEarly_SavesWhatWasRecorded()
	{
		MapTopic("node-1", 42);
		MapTopic("node-2", 43);
		_client.EditBehavior = (thread, _, _) => thread == 42
			? Task.FromResult(true)
			: throw ApiError(403, "Forbidden");
		var settings = new SaveCountingAppSettings(_appSettings);
		var svc = BuildReconcilingService(settings);

		svc.Start();
		await svc.ReconciliationTask;

		Assert.Equal(1, settings.SaveCount);
		Assert.True(_appSettings.Settings.TelegramTopicNamesByNodeId.ContainsKey("node-1"));
	}

	[Fact]
	public async Task Reconcile_StopDuringThrottleDelay_EndsPromptlyQuietly()
	{
		MapTopic("node-1", 42);
		MapTopic("node-2", 43);
		var svc = BuildReconcilingService();
		svc.DelayAsync = BlockUntilCancelled;

		svc.Start();
		await WaitUntil(() => _appSettings.Settings.TelegramTopicNamesByNodeId.Count == 1);
		svc.Stop();

		await svc.ReconciliationTask.WaitAsync(TimeSpan.FromSeconds(5));
		Assert.Single(_client.EditedTopics);
		Assert.Empty(Warnings());
	}

	[Fact]
	public async Task Reconcile_StopDuringRateLimitWait_EndsPromptlyQuietly()
	{
		MapTopic("node-1", 42);
		_client.EditBehavior = (_, _, _) => throw ApiError(429, "Too Many Requests: retry after 300", 300);
		var svc = BuildReconcilingService();
		svc.DelayAsync = BlockUntilCancelled;

		svc.Start();
		await WaitUntil(() => _client.EditedTopics.Count == 1);
		svc.Stop();

		await svc.ReconciliationTask.WaitAsync(TimeSpan.FromSeconds(5));
		Assert.Single(_client.EditedTopics);
		Assert.Empty(Warnings());
	}

	[Fact]
	public async Task Start_FirstEditBlocks_ReturnsImmediatelyAndPollingProceeds()
	{
		MapTopic("node-1", 42);
		var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		_client.EditBehavior = (_, _, _) => gate.Task;
		var svc = BuildReconcilingService();

		svc.Start();

		await WaitUntil(() => _client.EditedTopics.Count == 1);
		await WaitUntil(() => _client.Polls.Count > 0);
		Assert.False(svc.ReconciliationTask.IsCompleted);
		gate.SetResult(true);
		await svc.ReconciliationTask.WaitAsync(TimeSpan.FromSeconds(5));
	}

	[Fact]
	public async Task Start_StopThenStartAgainWhileFirstEditBlocked_SecondRunWaitsForFirstToEnd()
	{
		MapTopic("node-1", 42);
		var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var active = 0;
		var maxActive = 0;
		var calls = 0;
		_client.EditBehavior = async (_, _, _) =>
		{
			var n = Interlocked.Increment(ref active);
			maxActive = Math.Max(maxActive, n);
			try
			{
				if (Interlocked.Increment(ref calls) == 1)
				{
					await gate.Task; // ignores the stop token, like a request already on the wire
					throw ApiError(400, "Bad Request: TOPIC_ID_INVALID"); // not recorded, so a second run edits it again
				}
				return true;
			}
			finally { Interlocked.Decrement(ref active); }
		};
		var svc = BuildReconcilingService();

		svc.Start();
		await WaitUntil(() => _client.EditedTopics.Count == 1);
		svc.Stop();
		var firstRun = svc.ReconciliationTask;
		svc.Start();
		var secondRun = svc.ReconciliationTask;
		await Task.Delay(200);

		Assert.Single(_client.EditedTopics);
		Assert.False(firstRun.IsCompleted);
		Assert.NotSame(firstRun, secondRun);

		gate.SetResult(true);
		await secondRun.WaitAsync(TimeSpan.FromSeconds(5));

		Assert.True(firstRun.IsCompleted);
		Assert.Equal(2, _client.EditedTopics.Count);
		Assert.Equal(1, maxActive);
	}

	// ── Real DirectoryLabelService (TC.DL.01 "Callers") ──────────────────────────

	private static SessionNodeModel MakeHardStopNode(string nodeId) =>
		new() { NodeId = nodeId, Name = "Fix login", FileName = $"{nodeId}.txt", WorkingDirectory = @"C:\Projects\oDesk\HardStop\Code" };

	[Fact]
	public async Task EnsureTopicForSessionAsync_RealLabelService_NamesTopicWithShortDirectoryLabel()
	{
		var svc = BuildReconcilingService(labels: new DirectoryLabelService());

		await svc.EnsureTopicForSessionAsync(MakeHardStopNode("node-1"));

		Assert.Equal("HardStop / Fix login", _client.CreatedTopics.Single().name);
	}

	[Fact]
	public async Task Reconcile_RealLabelService_RenamesStoredFullPathNameToShortName()
	{
		_daemon.AddNode(MakeHardStopNode("node-1"));
		_appSettings.Settings.TelegramTopicThreadIdsByNodeId["node-1"] = 42;
		_appSettings.Settings.TelegramTopicNamesByNodeId["node-1"] = @"C:\Projects\oDesk\HardStop\Code / Fix login";
		var svc = BuildReconcilingService(labels: new DirectoryLabelService());

		svc.Start();
		await svc.ReconciliationTask;

		Assert.Equal("HardStop / Fix login", _client.EditedTopics.Single().name);
		Assert.Equal("HardStop / Fix login", _appSettings.Settings.TelegramTopicNamesByNodeId["node-1"]);
	}
}
