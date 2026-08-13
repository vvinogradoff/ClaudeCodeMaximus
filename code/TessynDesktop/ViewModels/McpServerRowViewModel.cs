using System;
using TessynDesktop.Models;
using TessynDesktop.Services;
using ReactiveUI;

namespace TessynDesktop.ViewModels;

/// <summary>
/// One row in the Settings window's "MCP Servers" list (FR.20.7). Wraps a live
/// <see cref="ExternalMcpServerModel"/> reference from <c>AppSettingsModel.ExternalMcpServers</c> —
/// toggling <see cref="Enabled"/> here writes straight through to the model and persists immediately,
/// independent of the Settings window's main Save button (unlike the rest of that window's fields).
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class McpServerRowViewModel : ViewModelBase
{
	private readonly IMcpTokenStore _tokenStore;
	private readonly Action _onEnabledChanged;
	private bool _enabled;
	private string _statusText = string.Empty;
	private string? _lastActionMessage;

	public ExternalMcpServerModel Model { get; }

	public string DisplayName => Model.DisplayName;

	public string TransportSummary => Model.Transport switch
	{
		McpServerTransport.Stdio => $"Stdio — {Model.Command}",
		McpServerTransport.Http  => $"Http — {Model.Url}",
		_                        => Model.Transport.ToString(),
	};

	public string AuthSummary => Model.Transport != McpServerTransport.Http
		? "n/a"
		: Model.AuthType switch
		{
			McpServerAuthType.None         => "No authentication",
			McpServerAuthType.StaticHeader => "Static header",
			McpServerAuthType.OAuth2       => "OAuth 2.1",
			_                               => Model.AuthType.ToString(),
		};

	/// <summary>Only Http + OAuth2 entries show the Authorize action (FR.20.4).</summary>
	public bool ShowAuthorizeButton => Model.Transport == McpServerTransport.Http && Model.AuthType == McpServerAuthType.OAuth2;

	public bool Enabled
	{
		get => _enabled;
		set
		{
			this.RaiseAndSetIfChanged(ref _enabled, value);
			Model.Enabled = value;
			_onEnabledChanged();
		}
	}

	public string StatusText
	{
		get => _statusText;
		private set => this.RaiseAndSetIfChanged(ref _statusText, value);
	}

	/// <summary>Outcome text of the most recent Authorize attempt, shown under the row until the next action.</summary>
	public string? LastActionMessage
	{
		get => _lastActionMessage;
		set => this.RaiseAndSetIfChanged(ref _lastActionMessage, value);
	}

	public McpServerRowViewModel(ExternalMcpServerModel model, IMcpTokenStore tokenStore, Action onEnabledChanged)
	{
		Model             = model;
		_tokenStore       = tokenStore;
		_onEnabledChanged = onEnabledChanged;
		_enabled          = model.Enabled;
		RefreshStatus();
	}

	/// <summary>Recomputes all model-derived display properties. Call after Edit or Authorize completes.</summary>
	public void RefreshStatus()
	{
		StatusText = ComputeStatus();
		this.RaisePropertyChanged(nameof(DisplayName));
		this.RaisePropertyChanged(nameof(TransportSummary));
		this.RaisePropertyChanged(nameof(AuthSummary));
		this.RaisePropertyChanged(nameof(ShowAuthorizeButton));
	}

	private string ComputeStatus()
	{
		if (Model.Transport != McpServerTransport.Http || Model.AuthType != McpServerAuthType.OAuth2)
			return "—";

		if (Model.OAuth?.NeedsReauthorization == true)
			return "Needs re-authorization";

		var record = _tokenStore.Load(Model.Id);
		if (record == null)
			return "Not connected";

		return string.IsNullOrEmpty(Model.OAuth?.ConnectedIdentity)
			? "Connected"
			: $"Connected as {Model.OAuth!.ConnectedIdentity}";
	}
}
