using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json.Nodes;
using Serilog;
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
	private readonly bool           _hasTreeMutation;
	private readonly bool           _hasSessionOptions;
	private readonly bool           _hasLocalTools;
	private readonly string          _daemonVersion;

	public ClientRpcModule(TimeProvider? time = null, IDaemonRunInfo? runInfo = null, IEventLog? eventLog = null, ISessionContentStore? contentStore = null, ITurnEngine? turnEngine = null, ITreeMutationService? treeMutation = null, ISessionOptionsService? sessionOptions = null, ILocalToolsService? localTools = null)
	{
		_time          = time ?? TimeProvider.System;
		_runInfo       = runInfo ?? new DaemonRunInfo();
		_eventLog      = eventLog;
		_turnEngine    = turnEngine;
		_hasContentStore = contentStore != null;
		_hasTreeMutation   = treeMutation != null;
		_hasSessionOptions = sessionOptions != null;
		_hasLocalTools     = localTools != null;
		_daemonVersion = ReadDaemonVersion();
	}

	/// <inheritdoc />
	public void Register(TdcpMethodRegistry registry) =>
		registry.AddSync(Constants.Remote.Methods.ClientHello, Hello, remoteCallable: true);

	private HelloResult Hello(JsonNode? @params)
	{
		var clientName    = TdcpParams.OptionalString(@params, "clientName");
		var clientVersion = TdcpParams.OptionalString(@params, "clientVersion");
		var clientProtocol = TdcpParams.OptionalInt(@params, "protocolVersion");
		ValidateCapabilities(TdcpParams.Find(@params, "capabilities"));

		if (clientProtocol is { } version && version < Constants.Remote.MinProtocolVersion)
			throw new TdcpInvalidParamsException(
				$"protocolVersion {version} is no longer supported (minimum {Constants.Remote.MinProtocolVersion})");

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
			_eventLog?.HeadSeq ?? 0);
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
		}

		// P1.F: entry ids need the content store.
		if (_hasContentStore)
			capabilities.Add(Constants.Remote.Capabilities.EntriesIds);

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
