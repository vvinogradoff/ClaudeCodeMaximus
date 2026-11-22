using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using TessynDesktop.Codex;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.Services;

/// <summary>
/// Remote-callable <c>client.hello</c>: lets a client learn the protocol version, the daemon's capabilities and the current
/// event seq, and detect a daemon restart. Optional params identify the client: <c>{ clientName, clientVersion, protocolVersion, capabilities }</c>.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class ClientRpcModule : IRemoteRpcModule
{
	private static readonly ILogger _log = Log.ForContext<ClientRpcModule>();

	private readonly TimeProvider    _time;
	private readonly IDaemonRunInfo  _runInfo;
	private readonly IEventLog?      _eventLog;
	private readonly ITurnEngine?    _turnEngine;
	private readonly bool           _hasContentStore;
	private readonly bool           _hasDrafts;
	private readonly bool           _hasCodeIndex;
	private readonly bool           _hasAuthErrors;
	private readonly IClientPresenceRegistry? _presence;
	private readonly INotificationAckBook? _acks;
	private readonly bool            _hasSettings;
	private readonly bool            _hasProfileAdmin;
	private readonly bool            _hasSound;
	private readonly bool            _hasTreeMutation;
	private readonly bool            _hasSessionOptions;
	private readonly bool            _hasLocalTools;
	private readonly bool            _hasImport;
	private readonly bool            _hasSearch;
	private readonly bool            _hasSchedules;
	private readonly bool            _hasAttentionAll;
	private readonly bool            _hasHealth;
	private readonly bool            _hasMcpRegistry;
	private readonly bool            _hasTelegramSettings;
	private readonly bool            _hasRelaySettings;
	private readonly bool            _hasModelsAdmin;
	private readonly string          _daemonVersion;
	private readonly bool            _elevated;
	private readonly ISessionEngineAvailability? _engines;
	private readonly bool            _hasCodexProfiles;
	private readonly CodexSettingsService? _codexSettings;
	private readonly Lock            _codexLock = new();
	private Task<CodexHelloStatusDto?>? _codexStatus;
	private DateTimeOffset           _codexStatusRead;

	public ClientRpcModule(TimeProvider? time = null, IDaemonRunInfo? runInfo = null, IEventLog? eventLog = null, ISessionContentStore? contentStore = null, ITurnEngine? turnEngine = null,
		ISettingsSectionService? settingsSections = null, IProfileAdminService? profileAdmin = null, ISessionSoundService? sounds = null,
		IDraftService? drafts = null, ICodeIndexService? codeIndex = null, ISessionAuthErrorTracker? authErrors = null,
		IClientPresenceRegistry? presence = null, INotificationAckBook? acks = null,
		ITreeMutationService? treeMutation = null, ISessionOptionsService? sessionOptions = null, ILocalToolsService? localTools = null,
		IImportService? import = null, IGlobalSearchService? search = null, IScheduleApiService? schedules = null,
		IAttentionOverviewService? attentionOverview = null, IDaemonHealthService? health = null,
		IMcpRegistryService? mcpRegistry = null, ITelegramTokenStore? telegramTokens = null, IRelayConfigStore? relayConfig = null, IClaudeModelService? models = null,
		bool? elevated = null, ISessionEngineAvailability? engines = null, TessynDesktop.Codex.ICodexProfileService? codexProfiles = null,
		CodexSettingsService? codexSettings = null)
	{
		_codexSettings    = codexSettings;     // FR.33.17: the read-only Codex status of client.hello, for every client
		_engines          = engines;           // FR.33 (tdcp-spec 10.A14): the engines whose CLI was found, on both transports
		_hasCodexProfiles = codexProfiles != null;
		_elevated      = elevated ?? Environment.IsPrivilegedProcess;   // FR.32.12: read once; a process cannot change its elevation
		_presence      = presence;
		_acks          = acks;
		_hasDrafts     = drafts != null;
		_hasCodeIndex  = codeIndex != null;
		_hasAuthErrors = authErrors != null;
		_time          = time ?? TimeProvider.System;
		_runInfo       = runInfo ?? new DaemonRunInfo();
		_eventLog      = eventLog;
		_turnEngine    = turnEngine;
		_hasContentStore = contentStore != null;
		_hasSettings     = settingsSections != null;
		_hasProfileAdmin = profileAdmin != null;
		_hasSound        = sounds != null;
		_hasTreeMutation   = treeMutation != null;
		_hasSessionOptions = sessionOptions != null;
		_hasLocalTools     = localTools != null;
		_hasImport         = import != null;
		_hasSearch         = search != null;
		_hasSchedules      = schedules != null;
		_hasAttentionAll   = attentionOverview != null;
		_hasHealth         = health != null;
		_hasMcpRegistry      = mcpRegistry != null;
		_hasTelegramSettings = telegramTokens != null;
		_hasRelaySettings    = relayConfig != null;
		_hasModelsAdmin      = models != null;
		_daemonVersion = ReadDaemonVersion();
	}

	/// <inheritdoc />
	public void Register(TdcpMethodRegistry registry)
	{
		registry.Add(Constants.Remote.Methods.ClientHello, async (@params, _) => Hello(@params, await CodexStatusAsync().ConfigureAwait(false)), remoteCallable: true);

		// Package J2: only a local client can present notifications, so the relay cannot register (-32601).
		if (_presence != null)
			registry.AddSync(RemoteProtocol.Notifications.RegisterMethod, RegisterClient, remoteCallable: false);

		if (_presence != null && _acks != null)
			registry.AddSync(RemoteProtocol.Notifications.AckMethod, AcknowledgeNotification, remoteCallable: false);
	}

	private ClientRegisterResult RegisterClient(JsonNode? @params)
	{
		var clientId = ReadClientId(@params, required: true)!;
		var presents = ReadPresents(@params) ?? throw new TdcpInvalidParamsException("'presents' is required");

		RegisterChecked(clientId, presents);
		return new ClientRegisterResult(true);
	}

	/// <summary>The registry is bounded (V7): a refusal for too many clients is an invalid-params error, not an internal one.</summary>
	private void RegisterChecked(string clientId, IReadOnlyCollection<string> presents)
	{
		try
		{
			_presence!.Register(clientId, presents);
		}
		catch (InvalidOperationException ex)
		{
			throw new TdcpInvalidParamsException(ex.Message);
		}
	}

	private NotificationAckResult AcknowledgeNotification(JsonNode? @params)
	{
		var notificationId = TdcpParams.OptionalString(@params, "notificationId");
		var nodeId         = TdcpParams.OptionalString(@params, "nodeId");
		if (string.IsNullOrEmpty(notificationId) == string.IsNullOrEmpty(nodeId))
			throw new TdcpInvalidParamsException("exactly one of 'notificationId' (a toast) and 'nodeId' (the completion sound) is required");

		if (!string.IsNullOrEmpty(notificationId))
			return new NotificationAckResult(_acks!.AcknowledgeToast(notificationId));

		_acks!.AcknowledgeSound(nodeId!);
		return new NotificationAckResult(true);
	}

	private static string? ReadClientId(JsonNode? @params, bool required)
	{
		var clientId = required ? TdcpParams.RequireString(@params, "clientId") : TdcpParams.OptionalString(@params, "clientId");
		if (clientId != null && (string.IsNullOrWhiteSpace(clientId) || clientId.Length > Constants.Composer.MaxClientIdLength))
			throw new TdcpInvalidParamsException($"'clientId' must be 1 to {Constants.Composer.MaxClientIdLength} characters");
		return clientId;
	}

	private static List<string>? ReadPresents(JsonNode? @params)
	{
		var node = TdcpParams.Find(@params, "presents");
		if (node == null)
			return null;

		if (node is not JsonArray array)
			throw new TdcpInvalidParamsException("'presents' must be an array of strings");

		var channels = new List<string>();
		foreach (var item in array)
		{
			if (item is not JsonValue value || !value.TryGetValue<string>(out var channel) || !RemoteProtocol.Notifications.Channels.Contains(channel))
				throw new TdcpInvalidParamsException($"'presents' may only contain: {string.Join(", ", RemoteProtocol.Notifications.Channels)}");
			if (!channels.Contains(channel))
				channels.Add(channel);
		}

		return channels;
	}

	/// <summary>
	/// The read-only Codex status (FR.33.17, tdcp-spec 10.A14): no path, no profile data. One reading is shared for
	/// <see cref="Constants.Remote.HelloCodexStatusLifetime"/>; a failed reading omits the member and is retried on the next call.
	/// </summary>
	private Task<CodexHelloStatusDto?> CodexStatusAsync()
	{
		if (_codexSettings == null)
			return Task.FromResult<CodexHelloStatusDto?>(null);

		lock (_codexLock)
		{
			var now = _time.GetUtcNow();
			var stale = _codexStatus == null
				|| (_codexStatus.IsCompleted && (_codexStatus.Result == null || now - _codexStatusRead >= Constants.Remote.HelloCodexStatusLifetime));
			if (stale)
			{
				_codexStatusRead = now;
				_codexStatus = ReadCodexStatusAsync(_codexSettings);
			}

			return _codexStatus!;
		}
	}

	private static async Task<CodexHelloStatusDto?> ReadCodexStatusAsync(CodexSettingsService settings)
	{
		try
		{
			var status = await settings.GetStatusAsync(CancellationToken.None).ConfigureAwait(false);
			return new CodexHelloStatusDto(status.Found, status.Version, status.TestedVersion, status.Note);
		}
		catch (Exception ex)
		{
			_log.Warning(ex, "client.hello could not read the Codex status");
			return null;
		}
	}

	private HelloResult Hello(JsonNode? @params, CodexHelloStatusDto? codex)
	{
		var clientName    = TdcpParams.OptionalString(@params, "clientName");
		var clientVersion = TdcpParams.OptionalString(@params, "clientVersion");
		var clientProtocol = TdcpParams.OptionalInt(@params, "protocolVersion");
		ValidateCapabilities(TdcpParams.Find(@params, "capabilities"));

		if (clientProtocol is { } version && version < Constants.Remote.MinProtocolVersion)
			throw new TdcpInvalidParamsException(
				$"protocolVersion {version} is no longer supported (minimum {Constants.Remote.MinProtocolVersion})");

		// Package J2: optional registration with the hello. Over the relay it is ignored: a remote client cannot present toasts on this machine.
		if (_presence != null && TdcpCallContext.Transport == TdcpTransport.Loopback)
		{
			var helloClientId = ReadClientId(@params, required: false);
			var helloPresents = ReadPresents(@params);
			if (helloClientId != null && helloPresents != null)
				RegisterChecked(helloClientId, helloPresents);
			else if (helloPresents != null)
				throw new TdcpInvalidParamsException("'presents' needs a 'clientId'");
		}

		if (clientName != null)
			_log.Debug("client.hello from {ClientName} {ClientVersion} (protocol {Protocol})", clientName, clientVersion, clientProtocol);

		return new HelloResult(
			Constants.Remote.ProtocolVersion,
			_daemonVersion,
			Environment.MachineName,
			_runInfo.RunId,
			_time.GetUtcNow(),
			Capabilities(),
			Constants.Remote.MinProtocolVersion,
			_eventLog?.HeadSeq ?? 0,
			_elevated,
			_engines?.GetAvailableEngines(),
			codex);
	}

	/// <summary>Only what is really wired: the event capabilities need the event log.</summary>
	private IReadOnlyList<string> Capabilities()
	{
		var capabilities = new List<string>();
		if (_eventLog != null)
		{
			capabilities.Add(Constants.Remote.Capabilities.EventsLocal);
			capabilities.Add(Constants.Remote.Capabilities.EventsResume);

			// The interactive turn stream (P1.I) needs the engine that publishes it and the log that carries it.
			if (_turnEngine != null)
				capabilities.Add(RemoteProtocol.TurnStream.Capability);

			// Package J2: per-run turn options (the engine runs Auto-Compact), the session auth error, drafts and autocomplete.
			if (_turnEngine != null)
				capabilities.Add(RemoteProtocol.Composer.TurnOptionsCapability);
			if (_hasAuthErrors)
				capabilities.Add(RemoteProtocol.Composer.AuthErrorCapability);
			if (_hasDrafts)
				capabilities.Add(RemoteProtocol.Composer.DraftsCapability);
			if (_hasCodeIndex)
				capabilities.Add(RemoteProtocol.Composer.AutocompleteCapability);
			if (_presence != null)
				capabilities.Add(RemoteProtocol.Notifications.Capability);
		}

		// P1.F: entry ids need the content store.
		if (_hasContentStore)
			capabilities.Add(Constants.Remote.Capabilities.EntriesIds);

		// FR.33: the result carries the engines (read-only, so the relay gets it too).
		if (_engines != null)
			capabilities.Add(RemoteProtocol.Engines.Capability);

		// The flags of J3, J1 and J4 name loopback-only methods (the relay answers -32601 for every one), so a relay client must not be told about them:
		// a web client that detects features by capability would show UI that cannot work.
		if (TdcpCallContext.Transport != TdcpTransport.Loopback)
			return capabilities;

		// Package J3: each flag needs the service behind its methods.
		if (_hasSettings)
			capabilities.Add(RemoteProtocol.SettingsApi.Capability);
		if (_hasProfileAdmin)
			capabilities.Add(RemoteProtocol.ProfilesAdmin.Capability);
		if (_hasProfileAdmin && _hasCodexProfiles)
			capabilities.Add(RemoteProtocol.CodexProfilesAdmin.Capability);
		if (_hasSound)
			capabilities.Add(RemoteProtocol.SoundApi.Capability);

		// P1.J1: the tree mutation, session option and local tool methods need their services.
		if (_hasTreeMutation)
		{
			capabilities.Add(RemoteProtocol.TreeMutation.Capability);
			capabilities.Add(RemoteProtocol.TreeMutation.InfoCapability);
		}

		if (_hasSessionOptions)
			capabilities.Add(RemoteProtocol.SessionOptions.Capability);

		if (_hasLocalTools)
			capabilities.Add(RemoteProtocol.LocalTools.Capability);

		// Package J4: search, import, schedules (with schedule.changed and hasSchedules), the attention list of all sessions, the health.
		if (_hasSearch)
			capabilities.Add(RemoteProtocol.Search.Capability);
		if (_hasImport)
			capabilities.Add(RemoteProtocol.Import.Capability);
		if (_hasSchedules)
			capabilities.Add(RemoteProtocol.Schedules.Capability);
		if (_hasAttentionAll)
			capabilities.Add(RemoteProtocol.AttentionAll.Capability);
		if (_hasHealth)
			capabilities.Add(RemoteProtocol.Health.Capability);

		// Package J3 part 2: the MCP registry, the Telegram token, the relay link and the model refresh.
		if (_hasMcpRegistry)
			capabilities.Add(RemoteProtocol.McpApi.Capability);
		if (_hasTelegramSettings)
			capabilities.Add(RemoteProtocol.TelegramApi.Capability);
		if (_hasRelaySettings)
			capabilities.Add(RemoteProtocol.RelayApi.Capability);
		if (_hasModelsAdmin)
			capabilities.Add(RemoteProtocol.ModelsApi.Capability);

		return capabilities;
	}

	private static void ValidateCapabilities(JsonNode? node)
	{
		if (node == null)
			return;

		if (node is not JsonArray array)
			throw new TdcpInvalidParamsException("'capabilities' must be an array of strings");

		foreach (var item in array)
		{
			if (item is not JsonValue value || !value.TryGetValue<string>(out _))
				throw new TdcpInvalidParamsException("'capabilities' must be an array of strings");
		}
	}

	private static string ReadDaemonVersion()
	{
		var assembly = Assembly.GetEntryAssembly() ?? typeof(ClientRpcModule).Assembly;
		var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
		if (!string.IsNullOrEmpty(informational))
		{
			// Strip the "+<commit>" build metadata the SDK appends.
			var plus = informational.IndexOf('+');
			return plus < 0 ? informational : informational[..plus];
		}

		return assembly.GetName().Version?.ToString() ?? string.Empty;
	}
}
