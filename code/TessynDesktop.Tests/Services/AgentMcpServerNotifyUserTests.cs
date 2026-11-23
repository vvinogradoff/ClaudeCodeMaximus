using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

internal sealed class FakeNotificationService : INotificationService
{
	public List<(string nodeId, string title, string message)> Toasts { get; } = new();
	public bool ToastResult { get; set; } = true;
	public bool ThrowOnToast { get; set; }

	public bool ShowUserNotification(string nodeId, string title, string message)
	{
		if (ThrowOnToast)
			throw new InvalidOperationException("toast system unavailable");
		Toasts.Add((nodeId, title, message));
		return ToastResult;
	}

	public void RegisterActivationHandler(Action<string> onNodeIdActivated) { }
}

internal sealed class FakeTelegramBridge : ITelegramBridgeService
{
	public List<(string nodeId, string message)> Notified { get; } = new();
	public bool NotifyResult { get; set; } = true;
	public bool ThrowOnNotify { get; set; }

	public void Start() { }
	public void Stop() { }
	public Task EnsureTopicForSessionAsync(SessionNodeModel node) => Task.CompletedTask;

	public Task<bool> NotifyAsync(string nodeId, string message)
	{
		if (ThrowOnNotify)
			throw new InvalidOperationException("telegram down");
		Notified.Add((nodeId, message));
		return Task.FromResult(NotifyResult);
	}
}

internal sealed class RecordingScheduler : ISchedulerService
{
	public event EventHandler? ScheduleChanged { add { } remove { } }
	public List<(string supervisor, string worker, string text)> Deliveries { get; } = new();
	public List<ScheduleModel> Added { get; } = new();

	public void Start() { }
	public void Stop() { }
	public void AddSchedule(ScheduleModel schedule) => Added.Add(schedule);

	public void AddOrMergeMailboxDelivery(string supervisorNodeId, string workerName, string resultText)
	{
		lock (Deliveries) Deliveries.Add((supervisorNodeId, workerName, resultText));
	}

	public List<(string target, string runId, string text)> ElevatedDeliveries { get; } = new();

	public void AddOrMergeElevatedRunDelivery(string targetNodeId, string runId, string outcomeText)
	{
		lock (ElevatedDeliveries) ElevatedDeliveries.Add((targetNodeId, runId, outcomeText));
	}

	public bool RemoveSchedule(string scheduleId) => false;
	public IReadOnlyList<ScheduleModel> GetSchedules(string? targetNodeId = null) => Array.Empty<ScheduleModel>();
}

/// <summary>
/// TC.21.17 — the <c>notify_user</c> channel fan-out (FR.21.5, FR.21.19), and TC.15.1 — the async
/// <c>send_to_session</c> path hands its result to the scheduler's mailbox merge (FR.15.12).
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class AgentMcpServerNotifyUserTests : IDisposable
{
	private readonly string _tempDir;
	private readonly FakeNotificationService _toasts = new();
	private readonly FakeTelegramBridge _telegram = new();
	private readonly RecordingScheduler _scheduler = new();
	private readonly FakeSessionRegistry _registry = new();
	private readonly FakeTurnService _turns = new();
	private readonly AttentionTestEnv _attention = new();
	private readonly AgentMcpServer _server;

	public AgentMcpServerNotifyUserTests()
	{
		_tempDir = Path.Combine(Path.GetTempPath(), $"cmx-notify-tests-{Guid.NewGuid():N}");
		Directory.CreateDirectory(_tempDir);
		_server = new AgentMcpServer(
			new AppSettingsService(_tempDir),
			new Lazy<ISessionTurnService>(() => _turns),
			new Lazy<ISchedulerService>(() => _scheduler),
			_registry,
			fileService: null!,
			modelService: null!,
			tdcpServer: null!,
			configMerger: null!,
			_telegram,
			_toasts,
			new FakeDirectoryLabelService(),
			_attention.Service,
			TagTestEnv.Unused());
	}

	public void Dispose()
	{
		_attention.Dispose();
		if (Directory.Exists(_tempDir))
			Directory.Delete(_tempDir, recursive: true);
	}

	private static SessionNodeModel Node(string id, int depth = 0) => new()
	{
		NodeId             = id,
		Name               = $"Session-{id}",
		FileName           = $"{id}.txt",
		WorkingDirectory   = @"C:\Proj\" + id,
		OrchestrationDepth = depth,
	};

	private Task<JsonNode> Call(string tool, JsonObject arguments, SessionNodeModel caller) =>
		_server.HandleToolCallAsync(new JsonObject { ["name"] = tool, ["arguments"] = arguments }, caller);

	private static string TextOf(JsonNode result) => result["content"]![0]!["text"]!.GetValue<string>();

	// FR.23.7 appends " Pinned as A<n>." / " Already pinned as A<n>." to the notify_user result; these
	// tests are about the channel sentence, so they compare it without that suffix.
	private static string ChannelTextOf(JsonNode result)
	{
		var text = TextOf(result);
		var cut  = text.IndexOf(" Pinned as ", StringComparison.Ordinal);
		if (cut < 0)
			cut = text.IndexOf(" Already pinned as ", StringComparison.Ordinal);
		return cut < 0 ? text : text[..cut];
	}

	private Task<JsonNode> Notify(SessionNodeModel caller, string message = "need you") =>
		Call("notify_user", new JsonObject { ["message"] = message }, caller);

	[Fact]
	public async Task NotifyUser_TopLevelSession_SendsTelegramAndShowsToast()
	{
		var caller = Node("top");

		await Notify(caller, "pick one");

		Assert.Equal(("top", "pick one"), Assert.Single(_telegram.Notified));
		var toast = Assert.Single(_toasts.Toasts);
		Assert.Equal("top", toast.nodeId);
		Assert.Equal(@"C:\Proj\top / Session-top", toast.title);
		Assert.Equal("pick one", toast.message);
	}

	[Fact]
	public async Task NotifyUser_WorkerSession_ReachesNoChannel()
	{
		var result = await Notify(Node("worker", depth: 1));

		Assert.Empty(_telegram.Notified);
		Assert.Empty(_toasts.Toasts);
		Assert.Contains("not sent", TextOf(result));
	}

	[Fact]
	public async Task NotifyUser_ToolResult_NamesDeliveredChannels()
	{
		var caller = Node("top");

		Assert.Equal("Notification sent (Telegram, desktop).", ChannelTextOf(await Notify(caller)));

		_toasts.ToastResult = false;
		Assert.Equal("Notification sent (Telegram).", ChannelTextOf(await Notify(caller)));

		_toasts.ToastResult = true;
		_telegram.NotifyResult = false;
		Assert.Equal("Notification sent (desktop).", ChannelTextOf(await Notify(caller)));

		_toasts.ToastResult = false;
		Assert.StartsWith("Notification not sent", ChannelTextOf(await Notify(caller)));
	}

	[Fact]
	public async Task NotifyUser_TelegramFails_ToastStillShown()
	{
		_telegram.ThrowOnNotify = true;

		var result = await Notify(Node("top"));

		Assert.Single(_toasts.Toasts);
		Assert.Equal("Notification sent (desktop).", ChannelTextOf(result));
	}

	[Fact]
	public async Task NotifyUser_ToastUnavailable_TelegramStillSent()
	{
		_toasts.ThrowOnToast = true;

		var result = await Notify(Node("top"));

		Assert.Single(_telegram.Notified);
		Assert.Equal("Notification sent (Telegram).", ChannelTextOf(result));
	}

	[Fact]
	public async Task SendToSessionAsync_AsyncMode_UsesAddOrMergeMailboxDelivery()
	{
		var supervisor = Node("sup");
		var worker = Node("wrk", depth: 1);
		_registry.AddNode(worker);

		await Call("send_to_session",
			new JsonObject { ["nodeId"] = "wrk", ["prompt"] = "do it", ["mode"] = "async" }, supervisor);

		for (var i = 0; i < 200 && _scheduler.Deliveries.Count == 0; i++)
			await Task.Delay(25);

		var delivery = Assert.Single(_scheduler.Deliveries);
		Assert.Equal("sup", delivery.supervisor);
		Assert.Equal("Session-wrk", delivery.worker);
		Assert.Equal("ok", delivery.text);
		Assert.Empty(_scheduler.Added);
	}
}
