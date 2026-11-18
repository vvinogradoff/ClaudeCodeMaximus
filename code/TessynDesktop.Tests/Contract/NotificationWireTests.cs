using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;
using TessynDesktop.Services;
using TessynDesktop.Tests.Services;
using TessynDesktop.Tests.Services.Turns;
using Xunit;

namespace TessynDesktop.Tests.Contract;

/// <summary>
/// Package J2 notifications on the wire: <c>client.register</c> and the optional hello params, the host's silence while a registered client's stream
/// is open and its return after a disconnect, the relay that cannot register, and <c>notification.raised</c> on the local stream.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class NotificationWireTests : IDisposable
{
	private readonly NotificationWireEnv _env = new();

	public void Dispose() => _env.Dispose();

	private static JsonObject Register(string clientId, params string[] presents) =>
		new() { ["clientId"] = clientId, ["presents"] = new JsonArray([.. presents.Select(p => (JsonNode?)JsonValue.Create(p))]) };

	private static int ErrorCode(JsonNode response) => response["error"]!["code"]!.GetValue<int>();

	private async Task<SseTestClient> OpenStreamAsync(string clientId) => await _env.Server.OpenStreamAsync("?clientId=" + clientId);

	private static async Task<bool> BecomesAsync(Func<bool> condition, bool expected)
	{
		var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
		while (DateTime.UtcNow < deadline)
		{
			if (condition() == expected)
				return true;
			await Task.Delay(25);
		}

		return condition() == expected;
	}

	// -- Registration -------------------------------------------------------------------------------------------------------------

	[Fact]
	public async Task Hello_AnnouncesNotificationsLocal_OnlyWithAPresenceRegistry()
	{
		var withIt = (await _env.Server.CallAsync("client.hello"))["result"]!["capabilities"]!.AsArray().Select(c => c!.GetValue<string>());
		using var without = new NotificationWireEnv(withPresence: false);
		var withoutIt = (await without.Server.CallAsync("client.hello"))["result"]!["capabilities"]!.AsArray().Select(c => c!.GetValue<string>());

		Assert.Contains("notifications.local", withIt);
		Assert.DoesNotContain("notifications.local", withoutIt);
	}

	[Fact]
	public async Task ClientRegister_AnswersOk_AndTheRegistrationCountsOnlyWithAnOpenStream()
	{
		var response = await _env.Server.CallAsync("client.register", Register("wpf", "toast", "sound"));

		Assert.True(response["result"]!["ok"]!.GetValue<bool>());
		Assert.False(_env.Presence.IsPresented("toast"));

		using var stream = await OpenStreamAsync("wpf");

		Assert.True(await BecomesAsync(() => _env.Presence.IsPresented("toast"), true));
		Assert.True(_env.Presence.IsPresented("sound"));
	}

	[Fact]
	public async Task HelloWithClientIdAndPresents_Registers_LikeClientRegister()
	{
		var hello = await _env.Server.CallAsync("client.hello", Register("wpf", "toast"));
		using var stream = await OpenStreamAsync("wpf");

		Assert.Null(hello["error"]);
		Assert.True(await BecomesAsync(() => _env.Presence.IsPresented("toast"), true));
		Assert.False(_env.Presence.IsPresented("sound"));
	}

	[Fact]
	public async Task ClientRegister_WithAnEmptyList_Unregisters()
	{
		await _env.Server.CallAsync("client.register", Register("wpf", "toast"));
		using var stream = await OpenStreamAsync("wpf");
		Assert.True(await BecomesAsync(() => _env.Presence.IsPresented("toast"), true));

		await _env.Server.CallAsync("client.register", Register("wpf"));

		Assert.False(_env.Presence.IsPresented("toast"));
	}

	[Fact]
	public async Task ClientRegister_Validation()
	{
		var noClient  = await _env.Server.CallAsync("client.register", new JsonObject { ["presents"] = new JsonArray() });
		var noPresent = await _env.Server.CallAsync("client.register", new JsonObject { ["clientId"] = "wpf" });
		var badChan   = await _env.Server.CallAsync("client.register", Register("wpf", "vibrate"));
		var longId    = await _env.Server.CallAsync("client.register", Register(new string('x', 65), "toast"));
		var notArray  = await _env.Server.CallAsync("client.register", new JsonObject { ["clientId"] = "wpf", ["presents"] = "toast" });
		var helloAlone = await _env.Server.CallAsync("client.hello", new JsonObject { ["presents"] = new JsonArray("toast") });

		Assert.All(new[] { noClient, noPresent, badChan, longId, notArray, helloAlone }, r => Assert.Equal(-32602, ErrorCode(r)));
	}

	[Fact]
	public async Task ClientRegister_BeyondTheMaximumOfClients_IsInvalidParams()
	{
		for (var i = 0; i < Constants.Composer.MaxPresenceClients; i++)
			await _env.Server.CallAsync("client.register", Register("client-" + i, "toast"));

		var refused = await _env.Server.CallAsync("client.register", Register("one-too-many", "toast"));
		var known = await _env.Server.CallAsync("client.register", Register("client-0", "sound"));

		Assert.Equal(-32602, ErrorCode(refused));
		Assert.Null(known["error"]);
	}

	// -- The relay cannot register ------------------------------------------------------------------------------------------------

	[Fact]
	public async Task ClientRegister_IsLoopbackOnly_TheRelayAnswersMethodNotFound()
	{
		var body = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = 1, ["method"] = "client.register", ["params"] = Register("wpf", "toast") }.ToJsonString();

		var response = await _env.Dispatcher.HandleTextAsync(body, TdcpTransport.Relay, CancellationToken.None);

		Assert.Equal(-32601, ErrorCode(response!));
		using var stream = await OpenStreamAsync("wpf");
		Assert.False(_env.Presence.IsPresented("toast"));
	}

	[Fact]
	public async Task HelloOverTheRelay_IgnoresPresents_EvenForAnOpenLocalStream()
	{
		using var stream = await OpenStreamAsync("wpf");
		var body = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = 1, ["method"] = "client.hello", ["params"] = Register("wpf", "toast", "sound") }.ToJsonString();

		var response = await _env.Dispatcher.HandleTextAsync(body, TdcpTransport.Relay, CancellationToken.None);

		Assert.Null(response!["error"]);
		Assert.False(_env.Presence.IsPresented("toast"));
		Assert.False(_env.Presence.IsPresented("sound"));
	}

	// -- Silent host, resume, no double -------------------------------------------------------------------------------------------

	[Fact]
	public async Task WhileARegisteredClientIsConnected_AndAcknowledges_TheHostIsSilent_AndTheClientGetsTheNotificationOnce()
	{
		await _env.Server.CallAsync("client.register", Register("wpf", "toast", "sound"));
		using var stream = await OpenStreamAsync("wpf");
		Assert.True(await BecomesAsync(() => _env.Presence.IsPresented("toast"), true));

		var shown = Task.Run(() => _env.Toasts.ShowUserNotification("node-1", "Build done", "All green"));
		var frame = await NextRaisedAsync(stream);
		var ack = await _env.Server.CallAsync("notification.ack", new JsonObject { ["notificationId"] = frame.Params!["notificationId"]!.GetValue<string>() });
		await _env.Server.CallAsync("notification.ack", new JsonObject { ["nodeId"] = "node-1" }); // the client played the Focus item sound
		_env.Sounds.PlayFocusItemSound("node-1");

		Assert.True(await shown.WaitAsync(TimeSpan.FromSeconds(10)));
		Assert.True(ack["result"]!["acknowledged"]!.GetValue<bool>());
		Assert.Equal("Build done", frame.Params["title"]!.GetValue<string>());
		await Task.Delay(NotificationWireEnv.AckTimeout * 2);
		Assert.Equal(0, _env.Host.ToastCount);
		Assert.Equal(0, _env.Host.SoundCount);
		await Assert.ThrowsAsync<TimeoutException>(() => stream.NextAsync(TimeSpan.FromMilliseconds(400)));
	}

	[Fact]
	public async Task AConnectedClientThatNeverAcknowledges_DoesNotLoseTheToastOrTheSound_TheHostShowsThemAfterTheTimeout()
	{
		await _env.Server.CallAsync("client.register", Register("wpf", "toast", "sound"));
		using var stream = await OpenStreamAsync("wpf"); // connected on the wire, but hung
		Assert.True(await BecomesAsync(() => _env.Presence.IsPresented("toast"), true));

		var shown = await Task.Run(() => _env.Toasts.ShowUserNotification("node-1", "Build done", "All green"));
		_env.Sounds.PlayFocusItemSound("node-1");

		Assert.True(shown);
		Assert.Equal(1, _env.Host.ToastCount);
		Assert.True(await BecomesAsync(() => _env.Host.SoundCount == 1, true));
	}

	[Fact]
	public async Task NotificationAck_Validation_AndTheRelayCannotSendIt()
	{
		var neither = await _env.Server.CallAsync("notification.ack", new JsonObject());
		var both    = await _env.Server.CallAsync("notification.ack", new JsonObject { ["notificationId"] = "a", ["nodeId"] = "n" });
		var unknown = await _env.Server.CallAsync("notification.ack", new JsonObject { ["notificationId"] = "nobody-waits" });
		var body = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = 1, ["method"] = "notification.ack", ["params"] = new JsonObject { ["nodeId"] = "n" } }.ToJsonString();
		var relay = await _env.Dispatcher.HandleTextAsync(body, TdcpTransport.Relay, CancellationToken.None);

		Assert.Equal(-32602, ErrorCode(neither));
		Assert.Equal(-32602, ErrorCode(both));
		Assert.False(unknown["result"]!["acknowledged"]!.GetValue<bool>());
		Assert.Equal(-32601, ErrorCode(relay!));
		Assert.False(_env.Acks.TryConsumeSoundAck("n", DateTimeOffset.UtcNow.AddMinutes(-1)));
	}

	[Fact]
	public async Task WhenTheClientDisconnectsWithoutUnregistering_TheHostResumes()
	{
		await _env.Server.CallAsync("client.register", Register("wpf", "toast", "sound"));
		var stream = await OpenStreamAsync("wpf");
		Assert.True(await BecomesAsync(() => _env.Presence.IsPresented("toast"), true));

		stream.Dispose(); // the process died: no client.register with an empty list

		Assert.True(await BecomesAsync(() => _env.Presence.IsPresented("toast"), false), "the dead stream still silences the host");
		_env.Toasts.ShowUserNotification("node-1", "After", "x");
		_env.Sounds.PlayFocusItemSound("node-1");
		Assert.Equal(1, _env.Host.ToastCount); // no client presents: the host does not wait
		Assert.Equal(1, _env.Host.SoundCount);
	}

	[Fact]
	public async Task AReconnectWithTheSameClientId_SilencesTheHostAgain_WithoutRegisteringAgain()
	{
		await _env.Server.CallAsync("client.register", Register("wpf", "toast"));
		var first = await OpenStreamAsync("wpf");
		Assert.True(await BecomesAsync(() => _env.Presence.IsPresented("toast"), true));
		first.Dispose();
		Assert.True(await BecomesAsync(() => _env.Presence.IsPresented("toast"), false));

		using var second = await OpenStreamAsync("wpf");

		Assert.True(await BecomesAsync(() => _env.Presence.IsPresented("toast"), true));
	}

	[Fact]
	public async Task AStreamOfAnotherOrNoClientId_DoesNotCountForTheRegistration()
	{
		await _env.Server.CallAsync("client.register", Register("wpf", "toast"));

		using var other = await OpenStreamAsync("web");
		using var anonymous = await _env.Server.OpenStreamAsync();
		await Task.Delay(300);

		Assert.False(_env.Presence.IsPresented("toast"));
	}

	[Fact]
	public async Task TheStreamKeepAlive_DoesNotDisturbTheFramesOfAnOpenStream()
	{
		using var stream = await OpenStreamAsync("wpf");
		await Task.Delay(450); // several keep-alive comment lines

		_env.Bus.Publish("session.changed", new { id = "n" });

		var frame = await stream.NextAsync();
		Assert.Equal("session.changed", frame.Method);
	}

	// -- V4: nothing is left behind by a finished stream ---------------------------------------------------------------------------

	[Fact]
	public async Task AfterManyStreamsWereDropped_NoKeepAliveLoopIsLeft()
	{
		const int Streams = 12;
		for (var i = 0; i < Streams; i++)
		{
			var stream = await OpenStreamAsync("wpf");
			await Task.Delay(30);
			stream.Dispose();
		}

		Assert.True(await BecomesAsync(() => _env.Server.Server.ActiveKeepAlives == 0, true),
			$"{_env.Server.Server.ActiveKeepAlives} keep-alive loops are still running");
	}

	[Fact]
	public async Task OpenStreamsHaveOneKeepAliveLoopEach()
	{
		using var a = await OpenStreamAsync("a");
		using var b = await OpenStreamAsync("b");

		Assert.True(await BecomesAsync(() => _env.Server.Server.ActiveKeepAlives == 2, true));
	}

	// -- notification.raised ------------------------------------------------------------------------------------------------------

	[Fact]
	public async Task NotificationRaised_ReachesEveryLocalStream_AndIsLocalOnly()
	{
		using var stream = await OpenStreamAsync("wpf");

		_env.Toasts.ShowUserNotification("node-1", "Title", "Message");

		var frame = await NextRaisedAsync(stream);
		Assert.Equal("node-1", frame.Params!["nodeId"]!.GetValue<string>());
		Assert.Equal("Message", frame.Params["message"]!.GetValue<string>());
		Assert.Equal("userNotification", frame.Params["kind"]!.GetValue<string>());
		Assert.True(RemoteProtocol.TurnStream.IsLocalOnly(new RemoteNotification(frame.Method!, frame.Params)));
		Assert.Equal(1, _env.Host.ToastCount); // nobody registered: the host shows it
	}

	// -- Focus item sound (FR.22.6, TC.SD.08) --------------------------------------------------------------------------------------

	private (FocusItemSoundNotifier Notifier, AttentionTestEnv Attention, string Dir) FocusSoundDaemon()
	{
		var dir = Path.Combine(Path.GetTempPath(), "focus-wire-tests-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(dir);
		var attention = new AttentionTestEnv();
		var registry = new FakeSessionRegistry();
		registry.AddNode(AttentionTestEnv.Node("node-1"));
		var notifier = new FocusItemSoundNotifier(attention.Service, registry, new AppSettingsService(dir), _env.Presence, _env.Bus, _env.Sounds, attention.Clock);
		notifier.Start();
		return (notifier, attention, dir);
	}

	[Fact]
	public async Task ANewFocusItem_ReachesAPresentingClientAsFocusItemCreated_WhichAcknowledgesIt_SoTheHostStaysSilent()
	{
		await _env.Server.CallAsync("client.register", Register("wpf", "sound"));
		using var stream = await OpenStreamAsync("wpf");
		Assert.True(await BecomesAsync(() => _env.Presence.IsPresented("sound"), true));
		var (notifier, attention, dir) = FocusSoundDaemon();
		using (notifier) using (attention)
		{
			var item = attention.Service.Post(AttentionTestEnv.Node("node-1"), [AttentionTestEnv.Req("Pick one", AttentionKind.Decided)]).Single().Item;

			SseTestFrame frame;
			do { frame = await stream.NextAsync(); } while (frame.Method != RemoteProtocol.Notifications.FocusItemCreatedEvent);
			await _env.Server.CallAsync("notification.ack", new JsonObject { ["nodeId"] = frame.Params!["nodeId"]!.GetValue<string>() });

			Assert.Equal("node-1", frame.Params["nodeId"]!.GetValue<string>());
			Assert.Equal(item.Id, frame.Params["itemId"]!.GetValue<string>());
			Assert.Equal("decided", frame.Params["kind"]!.GetValue<string>());
			Assert.True(RemoteProtocol.TurnStream.IsLocalOnly(new RemoteNotification(frame.Method!, frame.Params)));
			await Task.Delay(NotificationWireEnv.AckTimeout * 2);
			Assert.Equal(0, _env.Host.SoundCount);
		}

		Directory.Delete(dir, recursive: true);
	}

	[Fact]
	public async Task ANewFocusItem_WithAHungClient_IsPlayedByTheHostAfterTheTimeout()
	{
		await _env.Server.CallAsync("client.register", Register("wpf", "sound"));
		using var stream = await OpenStreamAsync("wpf");
		Assert.True(await BecomesAsync(() => _env.Presence.IsPresented("sound"), true));
		var (notifier, attention, dir) = FocusSoundDaemon();
		using (notifier) using (attention)
		{
			attention.Service.Post(AttentionTestEnv.Node("node-1"), [AttentionTestEnv.Req("Pick one")]);

			Assert.True(await BecomesAsync(() => _env.Host.SoundCount == 1, true));
		}

		Directory.Delete(dir, recursive: true);
	}

	private static async Task<SseTestFrame> NextRaisedAsync(SseTestClient stream)
	{
		while (true)
		{
			var frame = await stream.NextAsync();
			if (frame.Method == "notification.raised")
				return frame;
		}
	}
}
