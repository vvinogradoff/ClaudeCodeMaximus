using System;
using TessynDesktop.Models;
using ReactiveUI;

namespace TessynDesktop.ViewModels;

/// <summary>
/// Editable draft backing the Add/Edit MCP Server dialog (FR.20.7). Constructed from an existing
/// <see cref="ExternalMcpServerModel"/> when editing, or blank when adding. <see cref="TryBuildModel"/>
/// validates and produces the model to persist — the caller (Settings window) decides whether that
/// replaces an existing registry entry or appends a new one.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class McpServerEditViewModel : ViewModelBase
{
	private readonly string _id;
	private readonly bool _isNew;
	private readonly McpOAuthConfig? _existingOAuth;

	private string _displayName = string.Empty;
	private bool _enabled = true;
	private McpServerTransport _transport = McpServerTransport.Http;
	private string _command = string.Empty;
	private string _arguments = string.Empty;
	private string _workingDirectory = string.Empty;
	private string _url = string.Empty;
	private McpServerAuthType _authType = McpServerAuthType.None;
	private string _staticHeaderName = string.Empty;
	private string _staticHeaderValue = string.Empty;
	private string _authorizationEndpoint = string.Empty;
	private string _tokenEndpoint = string.Empty;
	private string _registrationEndpoint = string.Empty;
	private string _clientId = string.Empty;
	private string _clientSecret = string.Empty;
	private string _scopes = string.Empty;

	public string WindowTitle => _isNew ? "Add MCP Server" : "Edit MCP Server";

	public McpServerTransport[] TransportOptions { get; } = Enum.GetValues<McpServerTransport>();
	public McpServerAuthType[] AuthTypeOptions { get; } = Enum.GetValues<McpServerAuthType>();

	public string DisplayName
	{
		get => _displayName;
		set => this.RaiseAndSetIfChanged(ref _displayName, value);
	}

	public bool Enabled
	{
		get => _enabled;
		set => this.RaiseAndSetIfChanged(ref _enabled, value);
	}

	public McpServerTransport Transport
	{
		get => _transport;
		set
		{
			this.RaiseAndSetIfChanged(ref _transport, value);
			this.RaisePropertyChanged(nameof(IsStdio));
			this.RaisePropertyChanged(nameof(IsHttp));
		}
	}

	public bool IsStdio => Transport == McpServerTransport.Stdio;
	public bool IsHttp  => Transport == McpServerTransport.Http;

	public string Command
	{
		get => _command;
		set => this.RaiseAndSetIfChanged(ref _command, value);
	}

	public string Arguments
	{
		get => _arguments;
		set => this.RaiseAndSetIfChanged(ref _arguments, value);
	}

	public string WorkingDirectory
	{
		get => _workingDirectory;
		set => this.RaiseAndSetIfChanged(ref _workingDirectory, value);
	}

	public string Url
	{
		get => _url;
		set => this.RaiseAndSetIfChanged(ref _url, value);
	}

	public McpServerAuthType AuthType
	{
		get => _authType;
		set
		{
			this.RaiseAndSetIfChanged(ref _authType, value);
			this.RaisePropertyChanged(nameof(IsStaticHeader));
			this.RaisePropertyChanged(nameof(IsOAuth2));
		}
	}

	public bool IsStaticHeader => AuthType == McpServerAuthType.StaticHeader;
	public bool IsOAuth2       => AuthType == McpServerAuthType.OAuth2;

	public string StaticHeaderName
	{
		get => _staticHeaderName;
		set => this.RaiseAndSetIfChanged(ref _staticHeaderName, value);
	}

	public string StaticHeaderValue
	{
		get => _staticHeaderValue;
		set => this.RaiseAndSetIfChanged(ref _staticHeaderValue, value);
	}

	public string AuthorizationEndpoint
	{
		get => _authorizationEndpoint;
		set => this.RaiseAndSetIfChanged(ref _authorizationEndpoint, value);
	}

	public string TokenEndpoint
	{
		get => _tokenEndpoint;
		set => this.RaiseAndSetIfChanged(ref _tokenEndpoint, value);
	}

	public string RegistrationEndpoint
	{
		get => _registrationEndpoint;
		set => this.RaiseAndSetIfChanged(ref _registrationEndpoint, value);
	}

	public string ClientId
	{
		get => _clientId;
		set => this.RaiseAndSetIfChanged(ref _clientId, value);
	}

	public string ClientSecret
	{
		get => _clientSecret;
		set => this.RaiseAndSetIfChanged(ref _clientSecret, value);
	}

	public string Scopes
	{
		get => _scopes;
		set => this.RaiseAndSetIfChanged(ref _scopes, value);
	}

	public string? ErrorMessage { get; private set; }

	public McpServerEditViewModel(ExternalMcpServerModel? existing)
	{
		_isNew = existing == null;
		_id    = existing?.Id ?? Guid.NewGuid().ToString("N");

		if (existing == null)
			return;

		_displayName       = existing.DisplayName;
		_enabled           = existing.Enabled;
		_transport         = existing.Transport;
		_command           = existing.Command;
		_arguments         = existing.Arguments;
		_workingDirectory  = existing.WorkingDirectory;
		_url               = existing.Url;
		_authType          = existing.AuthType;
		_staticHeaderName  = existing.StaticHeaderName;
		_staticHeaderValue = existing.StaticHeaderValue;
		_existingOAuth     = existing.OAuth;

		if (existing.OAuth != null)
		{
			_authorizationEndpoint = existing.OAuth.AuthorizationEndpoint;
			_tokenEndpoint         = existing.OAuth.TokenEndpoint;
			_registrationEndpoint  = existing.OAuth.RegistrationEndpoint ?? string.Empty;
			_clientId              = existing.OAuth.ClientId;
			_clientSecret          = existing.OAuth.ClientSecret ?? string.Empty;
			_scopes                = existing.OAuth.Scopes;
		}
	}

	/// <summary>Validates and builds the model to persist. Returns false (with <see cref="ErrorMessage"/> set) if invalid.</summary>
	public bool TryBuildModel(out ExternalMcpServerModel model)
	{
		var name = DisplayName.Trim();
		if (string.IsNullOrEmpty(name))
		{
			ErrorMessage = "Display name is required.";
			model = null!;
			return false;
		}

		if (Transport == McpServerTransport.Stdio && string.IsNullOrWhiteSpace(Command))
		{
			ErrorMessage = "Command is required for a Stdio server.";
			model = null!;
			return false;
		}

		if (Transport == McpServerTransport.Http && string.IsNullOrWhiteSpace(Url))
		{
			ErrorMessage = "URL is required for an Http server.";
			model = null!;
			return false;
		}

		var effectiveAuthType = Transport == McpServerTransport.Http ? AuthType : McpServerAuthType.None;

		model = new ExternalMcpServerModel
		{
			Id                = _id,
			DisplayName       = name,
			Enabled           = Enabled,
			Transport         = Transport,
			Command           = Command.Trim(),
			Arguments         = Arguments.Trim(),
			WorkingDirectory  = WorkingDirectory.Trim(),
			Url               = Url.Trim(),
			AuthType          = effectiveAuthType,
			StaticHeaderName  = StaticHeaderName.Trim(),
			StaticHeaderValue = StaticHeaderValue,
			OAuth             = effectiveAuthType == McpServerAuthType.OAuth2 ? BuildOAuthConfig() : null,
		};
		ErrorMessage = null;
		return true;
	}

	/// <summary>
	/// Preserves discovery/connection state (<see cref="McpOAuthConfig.DiscoveryCompleted"/>,
	/// <see cref="McpOAuthConfig.NeedsReauthorization"/>, <see cref="McpOAuthConfig.ConnectedIdentity"/>)
	/// from the entry being edited — editing endpoint text or the display name must not silently drop
	/// an existing authorization.
	/// </summary>
	private McpOAuthConfig BuildOAuthConfig() => new()
	{
		AuthorizationEndpoint = AuthorizationEndpoint.Trim(),
		TokenEndpoint         = TokenEndpoint.Trim(),
		RegistrationEndpoint  = string.IsNullOrWhiteSpace(RegistrationEndpoint) ? null : RegistrationEndpoint.Trim(),
		ClientId              = ClientId.Trim(),
		ClientSecret          = string.IsNullOrWhiteSpace(ClientSecret) ? null : ClientSecret,
		Scopes                = Scopes.Trim(),
		DiscoveryCompleted    = _existingOAuth?.DiscoveryCompleted ?? false,
		NeedsReauthorization  = _existingOAuth?.NeedsReauthorization ?? false,
		ConnectedIdentity     = _existingOAuth?.ConnectedIdentity,
	};
}
