using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.Services;

/// <summary>
/// <c>telegram.status</c>, <c>telegram.setToken</c> and <c>telegram.clearToken</c> (package J3 part 2). The bot token is write-only: it goes into the
/// DPAPI-protected store and is never echoed, not even in an error. Loopback only.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class TelegramSettingsRpcModule : IRemoteRpcModule
{
	[GeneratedRegex(Constants.IntegrationsApi.TelegramTokenPattern)]
	private static partial Regex TokenPattern();

	private readonly ITelegramTokenStore _tokens;
	private readonly ITelegramBridgeService _bridge;
	private readonly IAppSettingsService _settings;

	public TelegramSettingsRpcModule(ITelegramTokenStore tokens, ITelegramBridgeService bridge, IAppSettingsService settings)
	{
		_tokens   = tokens;
		_bridge   = bridge;
		_settings = settings;
	}

	/// <inheritdoc />
	public void Register(TdcpMethodRegistry registry)
	{
		registry.AddSync(RemoteProtocol.TelegramApi.StatusMethod, _ => Status(), remoteCallable: false);
		registry.AddSync(RemoteProtocol.TelegramApi.SetTokenMethod, SetToken, remoteCallable: false);
		registry.AddSync(RemoteProtocol.TelegramApi.ClearTokenMethod, _ => ClearToken(), remoteCallable: false);
	}

	private TelegramStatusDto Status()
	{
		bool enabled;
		lock (_settings.SyncRoot)
			enabled = _settings.Settings.TelegramEnabled;

		return new TelegramStatusDto(enabled, _bridge.IsRunning, !string.IsNullOrEmpty(_tokens.Load()), _bridge.LastError);
	}

	private object? SetToken(JsonNode? @params)
	{
		var token = TdcpParams.RequireString(@params, "token").Trim();

		// The token is part of every Bot API URL, so only its own characters are accepted; the message does not repeat it.
		if (!TokenPattern().IsMatch(token))
			throw new TdcpInvalidParamsException("'token' does not look like a Telegram bot token (digits, a colon, then letters, digits, '-' or '_')");

		_tokens.Save(token);

		// The client reads the token per call, so a running bridge picks it up; a stopped one starts if it is enabled (Start does nothing when it is not).
		_bridge.Start();
		return Status();
	}

	private object? ClearToken()
	{
		_tokens.Delete();
		_bridge.Stop(); // without a token every poll would fail
		return Status();
	}
}
