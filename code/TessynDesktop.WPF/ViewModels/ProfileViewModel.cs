using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TessynDesktop.Client;
using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Infrastructure;
using TessynDesktop.WPF.Services;

namespace TessynDesktop.WPF.ViewModels;

/// <summary>
/// The profile flyout (W5, FR.12): lists the profiles from <c>profile.list</c> and keeps the list current through <c>profiles.changed</c>; adds, renames and removes
/// a profile and starts its login. A failed call is shown in <see cref="StatusText"/>, never thrown. Contains no WPF types.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class ProfileViewModel : ObservableObject, IDisposable
{
	private readonly IDialogService _dialogs;
	private readonly IUiDispatcher _ui;
	private readonly ILogger<ProfileViewModel> _logger;
	private ITessynClient? _client;
	private int _refreshGeneration;
	private bool _statusIsRefreshError;

	[ObservableProperty]
	private bool _isOpen;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(HasStatus))]
	private string? _statusText;

	[ObservableProperty]
	private bool _isAvailable;

	/// <summary>True when the connected daemon announces <c>profiles.admin</c>.</summary>
	public bool IsSupported { get; private set; }

	/// <param name="urlLauncher">The system browser (FR.26.9) for a ChatGPT sign-in's address; without one the address is only shown.</param>
	/// <param name="clipboard">For the copy buttons of a ChatGPT sign-in with a code.</param>
	public ProfileViewModel(IDialogService dialogs, IUiDispatcher ui, ILogger<ProfileViewModel> logger, ISystemUrlLauncher? urlLauncher = null, IClipboardWriter? clipboard = null)
	{
		_dialogs = dialogs;
		_ui = ui;
		_logger = logger;
		_urlLauncher = urlLauncher;
		_clipboard = clipboard;
	}

	public ObservableCollection<ProfileRowViewModel> Profiles { get; } = [];

	public bool HasStatus => !string.IsNullOrEmpty(StatusText);

	/// <summary>Uses this client from now on (the connection was made or re-made) and reads the list.</summary>
	public Task AttachAsync(ITessynClient client)
	{
		if (_client != null)
			_client.NotificationReceived -= OnNotification;

		_client = client;
		client.NotificationReceived += OnNotification;

		// Only a daemon that announces profiles.admin serves profile.*: an older one, or a relay, would answer every call with an error.
		IsSupported = client.ServerInfo?.Capabilities.Contains(RemoteProtocol.ProfilesAdmin.Capability) == true;
		if (!IsSupported)
		{
			IsAvailable = false;
			return Task.CompletedTask;
		}

		return RefreshAsync();
	}

	/// <summary>Reads <c>profile.list</c>; a failure leaves the list as it was.</summary>
	[RelayCommand]
	public async Task RefreshAsync()
	{
		var client = _client;
		if (client == null || !IsSupported)
			return;

		// A burst of profiles.changed starts overlapping reads: only the newest one may apply its answer.
		var generation = Interlocked.Increment(ref _refreshGeneration);
		try
		{
			var result = await client.ProfileListAsync();
			_ui.Post(() =>
			{
				if (generation != Volatile.Read(ref _refreshGeneration))
					return;

				if (_statusIsRefreshError)
				{
					StatusText = null;
					_statusIsRefreshError = false;
				}

				Profiles.Clear();
				foreach (var profile in result.Profiles)
					Profiles.Add(new ProfileRowViewModel(profile));
				ApplyChatGptProfiles(result.CodexProfiles);

				IsAvailable = true;
			});
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Reading the profile list failed");
			_ui.Post(() =>
			{
				if (generation != Volatile.Read(ref _refreshGeneration))
					return;

				StatusText = ex.Message;
				_statusIsRefreshError = true;
			});
		}
	}

	[RelayCommand]
	public async Task AddAsync()
	{
		var name = await _dialogs.InputAsync(new InputDialogRequest(
			Constants.Profiles.AddTitle, Constants.Profiles.AddPrompt, string.Empty, Constants.DialogTexts.Ok, NameValidator));
		if (name == null)
			return;

		await RunAsync(async client =>
		{
			var profile = await client.ProfileAddAsync(name.Trim());
			// A new profile has no login yet: start it at once, as the Avalonia client does.
			var auth = await client.ProfileAuthenticateAsync(profile.Id);
			StatusText = string.Format(auth.Started ? Constants.Profiles.AuthStarted : Constants.Profiles.AuthAlreadyRunning, profile.DisplayName);
		});
	}

	[RelayCommand]
	public async Task RenameAsync(ProfileRowViewModel? row)
	{
		if (row is not { CanEdit: true })
			return;

		var name = await _dialogs.InputAsync(new InputDialogRequest(
			Constants.Profiles.RenameTitle, Constants.Profiles.RenamePrompt, row.DisplayName, Constants.DialogTexts.Ok, NameValidator));
		if (name == null || name.Trim() == row.DisplayName)
			return;

		await RunAsync(client => client.ProfileRenameAsync(row.Id, name.Trim()));
	}

	[RelayCommand]
	public async Task RemoveAsync(ProfileRowViewModel? row)
	{
		if (row is not { CanEdit: true })
			return;

		var confirmed = await _dialogs.ConfirmAsync(new ConfirmDialogRequest(
			Constants.Profiles.RemoveTitle, string.Format(Constants.Profiles.ConfirmRemove, row.DisplayName), Constants.Profiles.RemoveTitle, IsDestructive: true));
		if (!confirmed)
			return;

		await RunAsync(async client =>
		{
			var result = await client.ProfileRemoveAsync(row.Id);
			StatusText = result.PinnedSessionCount > 0
				? string.Format(Constants.Profiles.RemovedPinned, row.DisplayName, result.PinnedSessionCount)
				: string.Format(Constants.Profiles.RemovedNoPinned, row.DisplayName);
		});
	}

	/// <summary>Starts the login of a profile on the daemon's machine; <c>profiles.changed</c> reports the end.</summary>
	[RelayCommand]
	public async Task AuthenticateAsync(ProfileRowViewModel? row)
	{
		if (row == null)
			return;

		await AuthenticateByIdAsync(row.Id, row.DisplayName);
	}

	/// <summary>The same by id and name (the auth banner knows only the profile name of the session).</summary>
	public Task AuthenticateByIdAsync(string profileId, string displayName) => RunAsync(async client =>
	{
		var auth = await client.ProfileAuthenticateAsync(profileId);
		StatusText = string.Format(auth.Started ? Constants.Profiles.AuthStarted : Constants.Profiles.AuthAlreadyRunning, displayName);
	});

	/// <summary>The profile with this display name, as the tree rows and the session state carry only the name.</summary>
	public ProfileRowViewModel? FindByName(string? name) =>
		string.IsNullOrEmpty(name) ? null : Profiles.FirstOrDefault(p => string.Equals(p.DisplayName, name, StringComparison.OrdinalIgnoreCase));

	public void Dispose()
	{
		if (_client != null)
			_client.NotificationReceived -= OnNotification;
	}

	private static string? NameValidator(string text) => string.IsNullOrWhiteSpace(text) ? Constants.Profiles.NameRequired : null;

	private async Task RunAsync(Func<ITessynClient, Task> call)
	{
		var client = _client;
		if (client == null)
			return;

		_ui.Post(() =>
		{
			StatusText = null;
			_statusIsRefreshError = false;
		});
		try
		{
			await call(client);
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "A profile call failed");
			_ui.Post(() => StatusText = ex.Message);
			return;
		}

		await RefreshAsync();
	}

	private void OnNotification(object? sender, RemoteNotification notification)
	{
		if (notification.IsProfilesChanged())
			_ = RefreshAsync();
		else if (notification.TryGetCodexLoginCompleted(out var completed))
			OnCodexLoginCompleted(completed);
	}
}
