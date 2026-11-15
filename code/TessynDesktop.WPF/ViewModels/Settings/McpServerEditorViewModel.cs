using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TessynDesktop.Client;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.WPF.ViewModels.Settings;

/// <summary>
/// The Add/Edit dialog of an MCP server (FR.20). It saves itself with <c>mcp.upsert</c> when asked; secrets (header value, OAuth client secret) are write-only fields:
/// they start empty, an empty field keeps the stored value and the dialog only says whether one is stored. When the address of an OAuth server is typed, the OAuth endpoints are
/// looked up on the daemon (cancellable, with a time limit at the daemon); a stdio server carries a warning.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class McpServerEditorViewModel : ObservableObject, IDisposable
{
	private readonly ITessynClient _client;
	private readonly ILogger _logger;
	private readonly McpServerDto? _original;
	private CancellationTokenSource? _discovery;

	[ObservableProperty]
	private string _displayName = string.Empty;

	[ObservableProperty]
	private bool _enabled = true;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(IsHttp), nameof(IsStdio), nameof(ShowOauth), nameof(ShowStaticHeader))]
	private McpTransportDto _transport = McpTransportDto.Http;

	[ObservableProperty]
	private string _url = string.Empty;

	[ObservableProperty]
	private string _command = string.Empty;

	[ObservableProperty]
	private string _argumentsInput = string.Empty;

	[ObservableProperty]
	private bool _clearArguments;

	[ObservableProperty]
	private string _workingDirectory = string.Empty;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(ShowOauth), nameof(ShowStaticHeader))]
	private McpAuthTypeDto _authType = McpAuthTypeDto.None;

	[ObservableProperty]
	private string _staticHeaderName = string.Empty;

	/// <summary>Write-only: what the user typed for the header value. Empty keeps the stored one.</summary>
	[ObservableProperty]
	private string _staticHeaderValueInput = string.Empty;

	[ObservableProperty]
	private string _clientId = string.Empty;

	/// <summary>Write-only: what the user typed for the OAuth client secret. Empty keeps the stored one.</summary>
	[ObservableProperty]
	private string _clientSecretInput = string.Empty;

	[ObservableProperty]
	private string _scopes = string.Empty;

	[ObservableProperty]
	private string _authorizationEndpoint = string.Empty;

	[ObservableProperty]
	private string _tokenEndpoint = string.Empty;

	[ObservableProperty]
	private string _registrationEndpoint = string.Empty;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(HasDiscoveryText))]
	private string? _discoveryText;

	[ObservableProperty]
	private bool _isDiscovering;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(HasError))]
	private string? _errorText;

	[ObservableProperty]
	private bool _isSaving;

	/// <summary>True once <see cref="SaveAsync"/> stored the server; the window closes on it.</summary>
	[ObservableProperty]
	private bool _saved;

	public McpServerEditorViewModel(ITessynClient client, McpServerDto? server, ILogger logger)
	{
		_client = client;
		_logger = logger;
		_original = server;
		if (server == null)
			return;

		DisplayName = server.DisplayName;
		Enabled = server.Enabled;
		Transport = server.Transport;
		Url = server.Url ?? string.Empty;
		Command = server.Command ?? string.Empty;
		WorkingDirectory = server.WorkingDirectory ?? string.Empty;
		AuthType = server.AuthType;
		StaticHeaderName = server.StaticHeaderName ?? string.Empty;
		if (server.Oauth != null)
		{
			ClientId = server.Oauth.ClientId;
			Scopes = server.Oauth.Scopes;
			AuthorizationEndpoint = server.Oauth.AuthorizationEndpoint;
			TokenEndpoint = server.Oauth.TokenEndpoint;
			RegistrationEndpoint = server.Oauth.RegistrationEndpoint ?? string.Empty;
		}
	}

	public bool IsNew => _original == null;

	public string Title => IsNew ? Constants.Settings.McpAddTitle : Constants.Settings.McpEditTitle;

	public IReadOnlyList<SettingOption<McpTransportDto>> Transports { get; } =
	[
		new(McpTransportDto.Http, "HTTP (remote server)"),
		new(McpTransportDto.Stdio, "stdio (a local command)"),
	];

	public IReadOnlyList<SettingOption<McpAuthTypeDto>> AuthTypes { get; } =
	[
		new(McpAuthTypeDto.None, "None"),
		new(McpAuthTypeDto.StaticHeader, "Static header (API key)"),
		new(McpAuthTypeDto.Oauth2, "OAuth 2.1"),
	];

	public bool IsHttp => Transport == McpTransportDto.Http;

	public bool IsStdio => Transport == McpTransportDto.Stdio;

	public bool ShowStaticHeader => IsHttp && AuthType == McpAuthTypeDto.StaticHeader;

	public bool ShowOauth => IsHttp && AuthType == McpAuthTypeDto.Oauth2;

	public string StdioWarning => Constants.Settings.McpStdioWarning;

	public bool HasDiscoveryText => !string.IsNullOrEmpty(DiscoveryText);

	public bool HasError => !string.IsNullOrEmpty(ErrorText);

	/// <summary>True when the stored URL has a query that is not shown.</summary>
	public bool UrlHasHiddenQuery => _original?.UrlHasQuery == true;

	public bool HasStoredArguments => _original?.HasArguments == true;

	public string HeaderValueText => _original?.HasStaticHeaderValue == true ? Constants.Settings.McpSecretSet : Constants.Settings.McpSecretNotSet;

	public string ClientSecretText => _original?.Oauth?.HasClientSecret == true ? Constants.Settings.McpSecretSet : Constants.Settings.McpSecretNotSet;

	/// <summary>
	/// Looks up the OAuth endpoints of the typed address, as the Avalonia dialog did when the address box lost the focus. Does nothing unless this is an OAuth HTTP server with a valid
	/// address and without both endpoints. Starting it again cancels the lookup that runs.
	/// </summary>
	[RelayCommand]
	public async Task DiscoverAsync()
	{
		var url = Url.Trim();
		if (!ShowOauth || !IsHttpUrl(url) || (AuthorizationEndpoint.Length > 0 && TokenEndpoint.Length > 0))
			return;

		CancelDiscoveryCore();
		var source = _discovery = new CancellationTokenSource();
		IsDiscovering = true;
		DiscoveryText = Constants.Settings.McpDiscovering;
		try
		{
			var known = new McpDiscoverKnownEndpoints(
				AuthorizationEndpoint.Length > 0 ? AuthorizationEndpoint : null,
				TokenEndpoint.Length > 0 ? TokenEndpoint : null,
				RegistrationEndpoint.Length > 0 ? RegistrationEndpoint : null);
			var result = await _client.McpDiscoverAsync(url, known, source.Token);
			if (source.IsCancellationRequested)
				return;

			var config = result.Config;
			if (config.AuthorizationEndpoint.Length > 0)
				AuthorizationEndpoint = config.AuthorizationEndpoint;
			if (config.TokenEndpoint.Length > 0)
				TokenEndpoint = config.TokenEndpoint;
			if (!string.IsNullOrEmpty(config.RegistrationEndpoint))
				RegistrationEndpoint = config.RegistrationEndpoint;

			DiscoveryText = result.TimedOut
				? Constants.Settings.McpDiscoveryTimedOut
				: config.AuthorizationEndpoint.Length > 0 || config.TokenEndpoint.Length > 0 ? Constants.Settings.McpDiscoveryFound : Constants.Settings.McpDiscoveryNothing;
		}
		catch (OperationCanceledException)
		{
			if (ReferenceEquals(_discovery, source))
				DiscoveryText = null;
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "MCP discovery failed");
			if (!source.IsCancellationRequested)
				DiscoveryText = ex.Message;
		}
		finally
		{
			if (ReferenceEquals(_discovery, source))
			{
				_discovery = null;
				IsDiscovering = false;
			}

			source.Dispose();
		}
	}

	/// <summary>Stops a lookup in progress (the Cancel button next to the spinner, and closing the dialog).</summary>
	[RelayCommand]
	public void CancelDiscovery()
	{
		CancelDiscoveryCore();
		DiscoveryText = null;
	}

	private void CancelDiscoveryCore()
	{
		var running = _discovery;
		_discovery = null;
		IsDiscovering = false;
		try
		{
			running?.Cancel();
		}
		catch (ObjectDisposedException)
		{
			// The lookup finished at the same moment.
		}
	}

	/// <summary>Sends the server with <c>mcp.upsert</c>; true when it was stored. A refusal is shown as <see cref="ErrorText"/>.</summary>
	[RelayCommand]
	public Task SaveAsync() => TrySaveAsync();

	/// <summary>The same, answering whether the server was stored.</summary>
	public async Task<bool> TrySaveAsync()
	{
		ErrorText = null;
		McpServerUpsertRequest request;
		try
		{
			request = BuildRequest();
		}
		catch (SettingsInputException ex)
		{
			ErrorText = ex.Message;
			return false;
		}

		CancelDiscoveryCore();
		IsSaving = true;
		try
		{
			await _client.McpUpsertAsync(request);
			Saved = true;
			return true;
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Saving an MCP server failed");
			ErrorText = ex.Message;
			return false;
		}
		finally
		{
			IsSaving = false;
		}
	}

	/// <summary>The request for the daemon; for an existing server only what differs (a left-out member keeps its value). Throws <see cref="SettingsInputException"/> for an invalid entry.</summary>
	public McpServerUpsertRequest BuildRequest()
	{
		var name = DisplayName.Trim();
		if (name.Length == 0)
			throw new SettingsInputException(Constants.Settings.McpNameRequired);

		var url = Url.Trim();
		var command = Command.Trim();
		if (IsHttp && !IsHttpUrl(url))
			throw new SettingsInputException(Constants.Settings.McpUrlRequired);

		if (IsStdio && command.Length == 0)
			throw new SettingsInputException(Constants.Settings.McpCommandRequired);

		var authType = IsHttp ? AuthType : McpAuthTypeDto.None;
		var headerName = StaticHeaderName.Trim();
		if (authType == McpAuthTypeDto.StaticHeader && headerName.Length == 0)
			throw new SettingsInputException(Constants.Settings.McpHeaderNameRequired);

		var original = _original;
		var oauth = authType == McpAuthTypeDto.Oauth2 ? BuildOauth(original?.Oauth) : null;
		var workingDirectory = IsStdio ? WorkingDirectory.Trim() : string.Empty;
		var arguments = IsStdio ? ClearArguments ? string.Empty : ArgumentsInput.Length > 0 ? ArgumentsInput : null : null;
		var headerValue = authType == McpAuthTypeDto.StaticHeader && StaticHeaderValueInput.Length > 0 ? StaticHeaderValueInput : null;

		if (original == null)
		{
			return new McpServerUpsertRequest(
				DisplayName: name,
				Enabled: Enabled,
				Transport: Transport,
				Url: IsHttp ? url : null,
				Command: IsStdio ? command : null,
				Arguments: arguments,
				WorkingDirectory: workingDirectory.Length > 0 ? workingDirectory : null,
				AuthType: authType,
				StaticHeaderName: authType == McpAuthTypeDto.StaticHeader ? headerName : null,
				StaticHeaderValue: headerValue,
				Oauth: oauth);
		}

		var transportChanged = Transport != original.Transport;
		return new McpServerUpsertRequest(
			Id: original.Id,
			DisplayName: name != original.DisplayName ? name : null,
			Enabled: Enabled != original.Enabled ? Enabled : null,
			Transport: transportChanged ? Transport : null,
			Url: IsHttp && (transportChanged || url != (original.Url ?? string.Empty)) ? url : null,
			Command: IsStdio && (transportChanged || command != (original.Command ?? string.Empty)) ? command : null,
			Arguments: arguments,
			WorkingDirectory: IsStdio && workingDirectory != (original.WorkingDirectory ?? string.Empty) ? workingDirectory : null,
			AuthType: authType != original.AuthType ? authType : null,
			StaticHeaderName: authType == McpAuthTypeDto.StaticHeader && (headerName != (original.StaticHeaderName ?? string.Empty) || original.AuthType != authType) ? headerName : null,
			StaticHeaderValue: headerValue,
			Oauth: oauth);
	}

	private McpOAuthPatch? BuildOauth(McpOAuthDto? stored)
	{
		var patch = new McpOAuthPatch(
			AuthorizationEndpoint: Changed(AuthorizationEndpoint, stored?.AuthorizationEndpoint),
			TokenEndpoint: Changed(TokenEndpoint, stored?.TokenEndpoint),
			RegistrationEndpoint: Changed(RegistrationEndpoint, stored?.RegistrationEndpoint),
			ClientId: Changed(ClientId, stored?.ClientId),
			ClientSecret: ClientSecretInput.Length > 0 ? ClientSecretInput : null,
			Scopes: Changed(Scopes, stored?.Scopes));
		return patch == new McpOAuthPatch() ? null : patch;
	}

	private static string? Changed(string typed, string? stored)
	{
		var text = typed.Trim();
		return text != (stored ?? string.Empty) ? text : null;
	}

	private static bool IsHttpUrl(string text) =>
		Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && !string.IsNullOrEmpty(uri.Host);

	public void Dispose() => CancelDiscoveryCore();
}
