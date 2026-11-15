using System.ComponentModel;
using TessynDesktop.Client;

namespace TessynDesktop.WPF.ViewModels;

/// <summary>
/// Owns the profile parts of the window (W5) and ties them to the connection: the flyout, the switch with its cascade dialog and the auth banner. They attach when
/// the connection is ready, whether the host was created before or after that, and the banner follows the tree once the tree exists. Contains no WPF types.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class ProfileHost : IDisposable
{
	private readonly MainViewModel _main;
	private ITessynClient? _client;

	public ProfileHost(ConnectionViewModel connection, MainViewModel main, ProfileViewModel flyout, ProfileSwitchViewModel switcher, AuthBannerViewModel banner)
	{
		_main = main;
		Flyout = flyout;
		Switcher = switcher;
		Banner = banner;
		connection.ClientReady += OnClientReady;
		main.PropertyChanged += OnMainChanged;

		// The order of construction and of the first ClientReady is not guaranteed: pick up what is already there.
		if (connection.Client != null)
			OnClientReady(connection.Client);

		AttachTree();
	}

	public ProfileViewModel Flyout { get; }

	public ProfileSwitchViewModel Switcher { get; }

	public AuthBannerViewModel Banner { get; }

	private void OnClientReady(ITessynClient client)
	{
		if (ReferenceEquals(client, _client))
			return;

		_client = client;
		Switcher.Attach(client);
		_ = Flyout.AttachAsync(client);
	}

	private void OnMainChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName == nameof(MainViewModel.Tree))
			AttachTree();
	}

	private void AttachTree()
	{
		if (_main.Tree != null)
			Banner.AttachTree(_main.Tree);
	}

	public void Dispose()
	{
		Banner.Dispose();
		Flyout.Dispose();
	}
}
