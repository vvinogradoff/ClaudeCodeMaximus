using TessynDesktop.Client;

namespace TessynDesktop.WPF.ViewModels;

/// <summary>
/// Owns the profile parts of the window (W5) and ties them to the connection: the flyout and the switch with its cascade dialog. They attach when the
/// connection is ready, whether the host was created before or after that. Contains no WPF types.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class ProfileHost : IDisposable
{
	private ITessynClient? _client;

	public ProfileHost(ConnectionViewModel connection, ProfileViewModel flyout, ProfileSwitchViewModel switcher)
	{
		Flyout = flyout;
		Switcher = switcher;
		connection.ClientReady += OnClientReady;

		// The order of construction and of the first ClientReady is not guaranteed: pick up what is already there.
		if (connection.Client != null)
			OnClientReady(connection.Client);
	}

	public ProfileViewModel Flyout { get; }

	public ProfileSwitchViewModel Switcher { get; }

	private void OnClientReady(ITessynClient client)
	{
		if (ReferenceEquals(client, _client))
			return;

		_client = client;
		Switcher.Attach(client);
		_ = Flyout.AttachAsync(client);
	}

	public void Dispose() => Flyout.Dispose();
}
