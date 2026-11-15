using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TessynDesktop.Client;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.WPF.ViewModels.Settings;

/// <summary>
/// The Web access tab (FR.25): the link between this desktop and the Tessyn web server. Saved at once by its own button, as in the Avalonia client
/// (no restart needed). The link token is write-only: a field to type a new one, and a line saying whether one is stored. Not a versioned section, so it is not part of the window's Save.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class WebAccessSectionViewModel : SettingsSectionViewModel
{
	private readonly ITessynClient _client;
	private readonly ILogger<WebAccessSectionViewModel> _logger;
	private RelayStatusDto? _status;

	[ObservableProperty]
	private bool _enabled;

	[ObservableProperty]
	private string _url = string.Empty;

	/// <summary>What the user typed for the link token; empty keeps the stored one. Cleared once sent.</summary>
	[ObservableProperty]
	private string _linkTokenInput = string.Empty;

	[ObservableProperty]
	private string _tokenText = Constants.Settings.RelayTokenNotSet;

	[ObservableProperty]
	private string? _stateText;

	[ObservableProperty]
	private string? _lastError;

	public WebAccessSectionViewModel(ITessynClient client, ILogger<WebAccessSectionViewModel> logger)
	{
		_client = client;
		_logger = logger;
	}

	public override string Title => "Web access";

	public async Task InitializeAsync()
	{
		if (_client.ServerInfo?.Capabilities.Contains(RemoteProtocol.RelayApi.Capability) != true)
		{
			MakeUnavailable(RemoteProtocol.RelayApi.Capability);
			return;
		}

		await RefreshAsync();
	}

	/// <summary>Reads <c>relay.status</c>. The fields are only overwritten when the user has not started editing them.</summary>
	[RelayCommand]
	public async Task RefreshAsync()
	{
		try
		{
			var previous = _status;
			var status = await _client.RelayStatusAsync();
			_status = status;
			if (previous == null || (Enabled == previous.Enabled && Url == (previous.Url ?? string.Empty)))
			{
				Enabled = status.Enabled;
				Url = status.Url ?? string.Empty;
			}

			TokenText = status.HasToken ? Constants.Settings.RelayTokenSet : Constants.Settings.RelayTokenNotSet;
			StateText = status.Since == DateTimeOffset.UnixEpoch
				? string.Format(Constants.Settings.RelayStateFormat, status.State)
				: string.Format(Constants.Settings.RelayStateSinceFormat, status.State, status.Since.LocalDateTime);
			LastError = status.LastError;
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Reading the relay status failed");
			ErrorText = ex.Message;
		}
	}

	/// <summary>Sends the switch, the address and (when typed) the link token with <c>relay.configure</c>.</summary>
	[RelayCommand]
	public async Task SaveAsync()
	{
		ClearMessages();
		var url = Url.Trim();
		var token = LinkTokenInput.Trim();
		var hasToken = token.Length > 0 || _status?.HasToken == true;
		if (Enabled && url.Length == 0)
		{
			ErrorText = string.Format(Constants.Settings.SaveFailedFormat, "the web server URL is empty.");
			return;
		}

		if (Enabled && !hasToken)
		{
			ErrorText = string.Format(Constants.Settings.SaveFailedFormat, Constants.Settings.RelayTokenNeeded);
			return;
		}

		var request = new RelayConfigureRequest(
			Enabled: Enabled != _status?.Enabled ? Enabled : null,
			Url: url != (_status?.Url ?? string.Empty) ? url : null,
			LinkToken: token.Length > 0 ? token : null);
		if (request == new RelayConfigureRequest())
		{
			StatusText = Constants.Settings.NothingToSave;
			return;
		}

		try
		{
			var status = await _client.RelayConfigureAsync(request);
			LinkTokenInput = string.Empty;
			_status = status;
			Enabled = status.Enabled;
			Url = status.Url ?? string.Empty;
			TokenText = status.HasToken ? Constants.Settings.RelayTokenSet : Constants.Settings.RelayTokenNotSet;
			StateText = string.Format(Constants.Settings.RelayStateFormat, status.State);
			LastError = status.LastError;
			StatusText = Constants.Settings.RelaySaved;
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Configuring the relay failed");
			ErrorText = string.Format(Constants.Settings.SaveFailedFormat, ex.Message);
		}
	}
}
