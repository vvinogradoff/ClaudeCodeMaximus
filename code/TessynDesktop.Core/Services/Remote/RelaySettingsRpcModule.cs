using System;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.Services;

/// <summary>
/// <c>relay.status</c> and <c>relay.configure</c> (package J3 part 2): the web relay link. The link token is write-only. Loopback only: the relay carries the
/// web contract, so whoever can change where the daemon dials and with which token decides who can drive it from outside. For the same reason the stored
/// token is bound to the origin it was stored for: moving the URL to another origin needs the token sent again.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class RelaySettingsRpcModule : IRemoteRpcModule
{
	[GeneratedRegex(@"^[\x21-\x7E]{1,4096}$")]
	private static partial Regex LinkTokenPattern();

	private readonly IRelayConfigStore _store;
	private readonly Lazy<IRelayConnectorService> _connector;
	private readonly object _gate = new();

	/// <param name="store">The relay link configuration.</param>
	/// <param name="connector">
	/// Lazy: the connector needs the dispatcher, which holds every module, this one included; resolving it when the module is built would be a cycle.
	/// </param>
	public RelaySettingsRpcModule(IRelayConfigStore store, Lazy<IRelayConnectorService> connector)
	{
		_store     = store;
		_connector = connector;
	}

	/// <inheritdoc />
	public void Register(TdcpMethodRegistry registry)
	{
		registry.AddSync(RemoteProtocol.RelayApi.StatusMethod, _ => Status(), remoteCallable: false);
		registry.AddSync(RemoteProtocol.RelayApi.ConfigureMethod, Configure, remoteCallable: false);
	}

	private RelayStatusDto Status()
	{
		var config = _store.Load();
		var status = _connector.Value.Status;
		var url    = SafeUrl.ForDisplay(config?.Url, out _);

		return new RelayStatusDto(
			config?.Enabled ?? false,
			(RelayStateDto)(int)status.State,
			url.Length == 0 ? null : url,
			!string.IsNullOrEmpty(config?.Token),
			status.Since,
			Describe(status.LastError, config?.Token));
	}

	private object? Configure(JsonNode? @params)
	{
		var request = SettingsPatchReader.Read<RelayConfigureRequest>(@params ?? new JsonObject(), "params");

		lock (_gate)
		{
			var existing = _store.Load();
			var enabled  = request.Enabled ?? existing?.Enabled ?? false;
			var url      = request.Url != null ? ReadUrl(request.Url) : existing?.Url ?? string.Empty;
			var token    = request.LinkToken != null ? ReadToken(request.LinkToken) : existing?.Token ?? string.Empty;

			if (url.Length > 0 && request.Url != null && request.LinkToken == null && !string.IsNullOrEmpty(existing?.Token)
				&& SafeUrl.Origin(url) != SafeUrl.Origin(existing.Url))
				throw new TdcpInvalidParamsException("The URL moves to another host: send 'linkToken' again, the stored token is not sent to a new origin");

			if (enabled && (url.Length == 0 || token.Length == 0))
				throw new TdcpInvalidParamsException("Switching the link on needs a 'url' and a 'linkToken'");

			_store.Save(new RelayConfigModel { Enabled = enabled, Url = url, Token = token });
		}

		// As the Avalonia Settings window does: the restart waits for the running link to stop, so it is not done on the request.
		_ = Task.Run(() => _connector.Value.Restart());
		return Status();
	}

	/// <summary>An empty URL (clearing it) or an http(s) base URL without user info, query or fragment.</summary>
	private static string ReadUrl(string value)
	{
		var text = value.Trim();
		if (text.Length == 0)
			return text;

		if (text.Length > Constants.IntegrationsApi.MaxUrlLength || !SafeUrl.TryParse(text, out var uri) || uri.Query.Length > 1 || uri.Fragment.Length > 1)
			throw new TdcpInvalidParamsException("'url' must be the web server's http or https base URL, without user info, query or fragment");

		return text;
	}

	private static string ReadToken(string value)
	{
		var text = value.Trim();
		if (text.Length > 0 && !LinkTokenPattern().IsMatch(text))
			throw new TdcpInvalidParamsException("'linkToken' must be printable characters without spaces");

		return text;
	}

	/// <summary>The connector's error as a short text; the link token is cut out of it if it appears (it should not).</summary>
	private static string? Describe(string? error, string? token)
	{
		if (string.IsNullOrWhiteSpace(error))
			return null;

		var text = string.IsNullOrEmpty(token) ? error : error.Replace(token, "***", StringComparison.Ordinal);
		text = new string([.. text.Where(c => !char.IsControl(c))]);
		return text.Length > Constants.IntegrationsApi.MaxErrorLength ? text[..Constants.IntegrationsApi.MaxErrorLength] : text;
	}
}
