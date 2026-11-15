using TessynDesktop.Client;
using TessynDesktop.Models.Remote;
using TessynDesktop.Tests.Contract;

namespace TessynDesktop.Tests.Client;

/// <summary>The notification presence of <see cref="TessynClient"/> (package J2) against a real loopback daemon.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class NotificationClientTests : IDisposable
{
	private readonly NotificationWireEnv _env = new();

	public void Dispose() => _env.Dispose();

	private TessynClient NewClient(string? clientId, params string[] presents) =>
		new(new Uri(_env.Server.BaseUrl + Constants.Agent.TdcpEndpointPath), TdcpTestHost.Token,
			new TessynClientOptions { ClientId = clientId, Presents = presents });

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

	[Fact]
	public async Task ConnectWithPresents_SilencesTheHost_WhileConnected_AndTheHostResumesWhenTheClientIsDisposed()
	{
		var client = NewClient("wpf", "toast", "sound");
		await client.ConnectAsync();

		Assert.Contains("notifications.local", client.ServerInfo!.Capabilities);
		Assert.True(await BecomesAsync(() => _env.Presence.IsPresented("toast"), true));
		_env.Toasts.ShowUserNotification("n", "silent", "x");
		_env.Sounds.PlayCompletionSound("n");
		Assert.Equal(0, _env.Host.ToastCount);
		Assert.Equal(0, _env.Host.SoundCount);

		await client.DisposeAsync();

		Assert.True(await BecomesAsync(() => _env.Presence.IsPresented("toast"), false));
		_env.Toasts.ShowUserNotification("n", "audible", "y");
		Assert.Equal(1, _env.Host.ToastCount);
	}

	[Fact]
	public async Task AnAnonymousClient_RegistersNothing()
	{
		await using var client = NewClient(clientId: null);
		await client.ConnectAsync();

		_env.Toasts.ShowUserNotification("n", "t", "m");

		Assert.Equal(1, _env.Host.ToastCount);
	}

	[Fact]
	public async Task AClientIdWithoutPresents_DoesNotSilenceAnything()
	{
		await using var client = NewClient("wpf");
		await client.ConnectAsync();
		await Task.Delay(200);

		_env.Toasts.ShowUserNotification("n", "t", "m");

		Assert.Equal(1, _env.Host.ToastCount);
	}

	[Fact]
	public async Task ClientRegisterAsync_ChangesTheChannelsAtRuntime_AndAnEmptyListUnregisters()
	{
		await using var client = NewClient("wpf");
		await client.ConnectAsync();

		var result = await client.ClientRegisterAsync(["toast"]);

		Assert.True(result.Ok);
		Assert.True(await BecomesAsync(() => _env.Presence.IsPresented("toast"), true));
		Assert.False(_env.Presence.IsPresented("sound"));

		await client.ClientRegisterAsync([]);
		Assert.False(_env.Presence.IsPresented("toast"));
	}

	[Fact]
	public async Task ClientRegisterAsync_NeedsAClientId()
	{
		await using var client = NewClient(clientId: null);
		await client.ConnectAsync();

		await Assert.ThrowsAsync<InvalidOperationException>(() => client.ClientRegisterAsync(["toast"]));
	}

	[Fact]
	public async Task AnUnknownChannel_IsRefusedByTheDaemon()
	{
		await using var client = NewClient("wpf");
		await client.ConnectAsync();

		var error = await Assert.ThrowsAsync<TessynRpcException>(() => client.ClientRegisterAsync(["vibrate"]));

		Assert.Equal(RemoteProtocol.ErrorCodes.InvalidParams, error.Code);
	}

	[Fact]
	public async Task NotificationRaised_ArrivesAsATypedNotification()
	{
		await using var client = NewClient("wpf", "toast");
		var probe = new ClientProbe(client);
		await client.ConnectAsync();
		Assert.True(await BecomesAsync(() => _env.Presence.IsPresented("toast"), true));

		_env.Toasts.ShowUserNotification("node-7", "Done", "Finished the task");

		NotificationRaisedPayload? raised = null;
		while (raised == null)
			(await probe.NextNotificationAsync()).TryGetNotificationRaised(out raised);

		Assert.Equal(("node-7", "Done", "Finished the task", "userNotification"), (raised.NodeId, raised.Title, raised.Message, raised.Kind));
		Assert.True(DateTimeOffset.UtcNow - raised.RaisedUtc < TimeSpan.FromMinutes(1));
		Assert.False(string.IsNullOrEmpty(raised.NotificationId));
	}
}
