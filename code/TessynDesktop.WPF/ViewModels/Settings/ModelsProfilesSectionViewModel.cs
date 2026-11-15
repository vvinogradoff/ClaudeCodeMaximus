using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TessynDesktop.Client;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.WPF.ViewModels.Settings;

/// <summary>
/// The Models and profiles tab (FR.12): a button that asks the daemon to look at the local Ollama models again, and the profile list of the status-bar flyout
/// (<see cref="Profiles"/>, the same view model, so both stay in step) with add, rename, remove and sign-in.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class ModelsProfilesSectionViewModel : SettingsSectionViewModel
{
	private readonly ITessynClient _client;
	private readonly ILogger<ModelsProfilesSectionViewModel> _logger;

	[ObservableProperty]
	private bool _modelsSupported;

	[ObservableProperty]
	private bool _isRefreshing;

	public ModelsProfilesSectionViewModel(ITessynClient client, ProfileViewModel profiles, ILogger<ModelsProfilesSectionViewModel> logger)
	{
		_client = client;
		Profiles = profiles;
		_logger = logger;
		ModelsSupported = client.ServerInfo?.Capabilities.Contains(RemoteProtocol.ModelsApi.Capability) == true;
	}

	public override string Title => "Models and profiles";

	/// <summary>The profile list and its commands; null-safe to bind when profile administration is not offered (<c>Profiles.IsSupported</c>).</summary>
	public ProfileViewModel Profiles { get; }

	public string ProfilesHint => Constants.Settings.ProfilesWindowHint;

	[RelayCommand]
	public async Task RefreshModelsAsync()
	{
		if (!ModelsSupported || IsRefreshing)
			return;

		ClearMessages();
		IsRefreshing = true;
		try
		{
			var result = await _client.ModelsRefreshAsync();
			StatusText = string.Format(Constants.Settings.ModelsRefreshed, result.Count);
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Refreshing the models failed");
			ErrorText = ex.Message;
		}
		finally
		{
			IsRefreshing = false;
		}
	}
}
