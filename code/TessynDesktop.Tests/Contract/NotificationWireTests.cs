using System;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Models.Remote;
using TessynDesktop.Services;
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
	public async Task WhileARegisteredClientIsConnected_TheHostIsSilent_AndTheClientGetsTheNotificationOnce()
	{
		await _env.Server.CallAsync("client.register", Register("wpf", "toast", "sound"));
		using var stream = await OpenStreamAsync("wpf");
		Assert.True(await BecomesAsync(() => _env.Presence.IsPresented("toast"), true));

		_env.Toasts.ShowUserNotification("node-1", "Build done", "All green");
		_env.Sounds.PlayCompletionSound("node-1");

		var frame = await NextRaisedAsync(stream);
		Assert.Equal("Build done", frame.Params!["title"]!.GetValue<string>());
		Assert.Equal(0, _env.Host.ToastCount);
		Assert.Equal(0, _env.Host.SoundCount);
		await Assert.ThrowsAsync<TimeoutException>(() => stream.NextAsync(TimeSpan.FromMilliseconds(400)));
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
		_env.Sounds.PlayCompletionSound("node-1");
		Assert.Equal(1, _env.Host.ToastCount);
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
