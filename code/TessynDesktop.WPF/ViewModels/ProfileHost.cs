using System.ComponentModel;
using TessynDesktop.Client;

namespace TessynDesktop.WPF.ViewModels;

/// <summary>
/// Owns the profile parts of the window (W5) and ties them to the connection: the flyout and the switch with its cascade dialog. They attach when the
/// connection is ready, whether the host was created before or after that. It also tells the flyout which ChatGPT profile the open session uses and
/// whether it has an auth error (FR.33.13), so the flyout can offer Sign in again for it. Contains no WPF types.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class ProfileHost : IDisposable
{
	private ITessynClient? _client;
	private SessionPaneViewModel? _session;

	/// <param name="main">The window's view model; without one the flyout never shows a session's ChatGPT profile.</param>
	public ProfileHost(ConnectionViewModel connection, ProfileViewModel flyout, ProfileSwitchViewModel switcher, MainViewModel? main = null)
	{
		Flyout = flyout;
		Switcher = switcher;
		connection.ClientReady += OnClientReady;

		// The order of construction and of the first ClientReady is not guaranteed: pick up what is already there.
		if (connection.Client != null)
			OnClientReady(connection.Client);

		if (main != null)
		{
			main.PropertyChanged += (_, e) =>
			{
				if (e.PropertyName == nameof(MainViewModel.Session))
					Observe(main.Session);
			};
			Observe(main.Session);
		}
	}

	public ProfileViewModel Flyout { get; }

	public ProfileSwitchViewModel Switcher { get; }

	/// <summary>Follows the open session's ChatGPT profile and auth-error flag.</summary>
	public void Observe(SessionPaneViewModel? session)
	{
		if (ReferenceEquals(session, _session))
			return;

		if (_session != null)
			_session.PropertyChanged -= OnSessionChanged;

		_session = session;
		if (session != null)
			session.PropertyChanged += OnSessionChanged;

		ShowSession();
	}

	private void OnSessionChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName is nameof(SessionPaneViewModel.ChatGptProfileId) or nameof(SessionPaneViewModel.HasAuthError) or nameof(SessionPaneViewModel.HasSession))
			ShowSession();
	}

	private void ShowSession() =>
		Flyout.ShowSession(_session is { HasSession: true } ? _session.ChatGptProfileId : null, _session?.HasAuthError == true);

	private void OnClientReady(ITessynClient client)
	{
		if (ReferenceEquals(client, _client))
			return;

		_client = client;
		Switcher.Attach(client);
		_ = Flyout.AttachAsync(client);
	}

	public void Dispose()
	{
		if (_session != null)
			_session.PropertyChanged -= OnSessionChanged;
		Flyout.Dispose();
	}
}
