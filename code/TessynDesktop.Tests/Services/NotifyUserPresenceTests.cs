using System;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary>
/// V3 end to end through <c>notify_user</c>: the real toast fan-out behind the tool. The <c>desktop</c> channel is reported only when the host showed
/// the toast or a client acknowledged it; a presenting client that never answers cannot make the tool claim a delivery that did not happen.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class NotifyUserPresenceTests : IDisposable
{
	private static readonly TimeSpan AckTimeout = TimeSpan.FromMilliseconds(250);

	private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"cmx-notify-presence-{Guid.NewGuid():N}");
	private readonly FakeNotificationService _host = new();
	private readonly ClientPresenceRegistry _presence = new();
	private readonly NotificationAckBook _acks = new();
	private readonly RemoteEventBus _bus = new();
	private readonly FakeTelegramBridge _telegram = new();
	private readonly AttentionTestEnv _attention = new();
	private readonly AgentMcpServer _server;

	public NotifyUserPresenceTests()
	{
		Directory.CreateDirectory(_tempDir);
		var settings = new AppSettingsService(_tempDir);
		var fanOut = new NotificationFanOut(_host, _presence, _bus, settings, _acks, ackTimeout: AckTimeout);
		_server = new AgentMcpServer(
			settings,
			new Lazy<ISessionTurnService>(() => new FakeTurnService()),
			new Lazy<ISchedulerService>(() => new RecordingScheduler()),
			new FakeSessionRegistry(),
			fileService: null!, modelService: null!, tdcpServer: null!, configMerger: null!,
			_telegram, fanOut, new FakeDirectoryLabelService(), _attention.Service, TagTestEnv.Unused());
	}

	public void Dispose()
	{
		_bus.Dispose();
		_attention.Dispose();
		Directory.Delete(_tempDir, recursive: true);
	}

	private static SessionNodeModel Caller() => new() { NodeId = "top", Name = "Session-top", FileName = "top.txt", WorkingDirectory = @"C:\Proj\top" };

	private async Task<string> NotifyAsync()
	{
		var result = await _server.HandleToolCallAsync(
			new JsonObject { ["name"] = "notify_user", ["arguments"] = new JsonObject { ["message"] = "need you" } }, Caller());
		var text = result["content"]![0]!["text"]!.GetValue<string>();
		var cut = text.IndexOf(" Pinned as ", StringComparison.Ordinal);
		return cut < 0 ? text : text[..cut];
	}

	private void HungClientPresentsToasts()
	{
		_presence.Register("wpf", ["toast"]);
		_presence.OpenStream("wpf"); // connected, never answers
	}

	[Fact]
	public async Task AClientThatNeverAcknowledges_AndAHostToastThatFails_LeavesDesktopOutOfTheChannelList()
	{
		HungClientPresentsToasts();
		_host.ToastResult = false;
		_telegram.NotifyResult = true;

		Assert.Equal("Notification sent (Telegram).", await NotifyAsync());
	}

	[Fact]
	public async Task AClientThatNeverAcknowledges_GetsTheHostToastAfterTheTimeout_SoDesktopIsReported()
	{
		HungClientPresentsToasts();

		Assert.Equal("Notification sent (Telegram, desktop).", await NotifyAsync());
		Assert.Single(_host.Toasts);
	}

	[Fact]
	public async Task AnAcknowledgedToast_ReportsDesktop_WithoutAHostToast()
	{
		HungClientPresentsToasts();
		_bus.Published += n =>
		{
			if (n.Method == "notification.raised")
				_acks.AcknowledgeToast(n.Params["notificationId"]!.GetValue<string>());
		};

		Assert.Equal("Notification sent (Telegram, desktop).", await NotifyAsync());
		Assert.Empty(_host.Toasts);
	}
}
