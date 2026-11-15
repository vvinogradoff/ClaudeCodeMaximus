using CommunityToolkit.Mvvm.ComponentModel;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.WPF.ViewModels.Settings;

/// <summary>One row of the MCP server list: name, transport and authorization summary, state, and the message of the last action on it.</summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class McpServerRowViewModel : ObservableObject
{
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(HasMessage))]
	private string? _message;

	[ObservableProperty]
	private bool _isBusy;

	public McpServerRowViewModel(McpServerDto server)
	{
		Server = server;
	}

	public McpServerDto Server { get; }

	public string Id => Server.Id;

	public string DisplayName => Server.DisplayName;

	public bool Enabled => Server.Enabled;

	public string EnabledText => Server.Enabled ? "On" : "Off";

	public string ToggleText => Server.Enabled ? "Disable" : "Enable";

	public string TransportSummary => Server.Transport == McpTransportDto.Http
		? $"HTTP  {Server.Url}"
		: $"stdio  {Server.Command}{(Server.HasArguments ? " …" : string.Empty)}";

	public string AuthSummary => Server.AuthType switch
	{
		McpAuthTypeDto.StaticHeader => $"Header {Server.StaticHeaderName}{(Server.HasStaticHeaderValue ? " (value stored)" : " (no value)")}",
		McpAuthTypeDto.Oauth2 => "OAuth 2.1" + (string.IsNullOrEmpty(Server.ConnectedIdentity) ? string.Empty : $" as {Server.ConnectedIdentity}"),
		_ => "No authentication",
	};

	public string StatusText => Server.Status switch
	{
		McpServerStatusDto.Connected => "Connected",
		McpServerStatusDto.NotConnected => "Not connected",
		McpServerStatusDto.NeedsReauthorization => "Needs re-authorization",
		_ => string.Empty,
	};

	/// <summary>Only an OAuth server has anything to authorize.</summary>
	public bool CanAuthorize => Server.AuthType == McpAuthTypeDto.Oauth2;

	public string AuthorizeText => Server.Status == McpServerStatusDto.Connected ? "Re-authorize" : "Authorize";

	public bool HasMessage => !string.IsNullOrEmpty(Message);

	public string AutomationName => Server.DisplayName;
}
