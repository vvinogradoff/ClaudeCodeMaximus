using System.Threading.Channels;
using TessynDesktop.Client;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.Tests.Client;

/// <summary>Collects a client's notifications, resyncs and state changes so a test can await them with a timeout.</summary>
/// <remarks>Created by Claude</remarks>
internal sealed class ClientProbe
{
	public static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

	private readonly Channel<RemoteNotification> _notifications = Channel.CreateUnbounded<RemoteNotification>();
	private readonly Channel<ResyncedEventArgs> _resyncs = Channel.CreateUnbounded<ResyncedEventArgs>();
	private readonly Channel<TessynClientState> _states = Channel.CreateUnbounded<TessynClientState>();

	public ClientProbe(ITessynClient client)
	{
		client.NotificationReceived += (_, n) => _notifications.Writer.TryWrite(n);
		client.Resynced             += (_, r) => _resyncs.Writer.TryWrite(r);
		client.StateChanged         += (_, s) => _states.Writer.TryWrite(s.Current);
	}

	public Task<RemoteNotification> NextNotificationAsync() => ReadAsync(_notifications);
	public Task<ResyncedEventArgs> NextResyncAsync() => ReadAsync(_resyncs);

	/// <summary>Waits until the client reports <paramref name="state"/> (earlier changes are skipped).</summary>
	public async Task WaitForStateAsync(TessynClientState state)
	{
		while (await ReadAsync(_states) != state)
		{
		}
	}

	/// <summary>True when no notification arrives within <paramref name="quiet"/>.</summary>
	public async Task<bool> NotificationsStayQuietAsync(TimeSpan quiet)
	{
		using var cts = new CancellationTokenSource(quiet);
		try
		{
			await _notifications.Reader.WaitToReadAsync(cts.Token);
			return false;
		}
		catch (OperationCanceledException)
		{
			return true;
		}
	}

	private static async Task<T> ReadAsync<T>(Channel<T> channel)
	{
		using var cts = new CancellationTokenSource(Wait);
		try
		{
			return await channel.Reader.ReadAsync(cts.Token);
		}
		catch (OperationCanceledException)
		{
			throw new TimeoutException($"Timed out waiting for {typeof(T).Name}");
		}
	}
}
