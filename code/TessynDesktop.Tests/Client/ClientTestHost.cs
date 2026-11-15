using System.Net;
using System.Net.Sockets;
using TessynDesktop.Services;
using TessynDesktop.Tests.Contract;
using TessynDesktop.Tests.Services;

namespace TessynDesktop.Tests.Client;

/// <summary>
/// A real <see cref="TdcpServer"/> on a fixed loopback port for the client tests. Unlike <see cref="TdcpTestHost"/> the server can be
/// stopped and started again on the same port (a dropped connection), optionally as a new daemon run with a fresh event log.
/// </summary>
/// <remarks>Created by Claude</remarks>
internal sealed class ClientTestHost : IDisposable
{
	public const string Token = "client-test-token";

	private readonly Func<IDaemonRunInfo, IEventLog, TdcpDispatcher> _dispatcherFactory;
	private readonly int _logCapacity;
	private readonly IClientPresenceRegistry? _presence;
	private readonly WireFakeDaemonClient _daemonClient = new();

	public ClientTestHost(Func<IDaemonRunInfo, IEventLog, TdcpDispatcher>? dispatcherFactory = null, int logCapacity = Constants.Remote.EventLog.DefaultCapacity, IClientPresenceRegistry? presence = null)
	{
		_dispatcherFactory = dispatcherFactory ?? ((run, log) => new TdcpDispatcher([new ClientRpcModule(runInfo: run, eventLog: log)]));
		_logCapacity       = logCapacity;
		_presence          = presence;

		// A free port is only known to be free until it is released: another test class may take it before the bind, so try again.
		for (var attempt = 1; ; attempt++)
		{
			var listener = new TcpListener(IPAddress.Loopback, 0);
			listener.Start();
			Port = ((IPEndPoint)listener.LocalEndpoint).Port;
			listener.Stop();

			try
			{
				StartRun();
				return;
			}
			catch (HttpListenerException) when (attempt < 10)
			{
				DisposeRun();
			}
		}
	}

	public int Port { get; private set; }
	public Uri Endpoint => new($"http://127.0.0.1:{Port}{Constants.Agent.TdcpEndpointPath}");
	public RemoteEventBus Bus { get; private set; } = null!;
	public EventLog Log { get; private set; } = null!;
	public DaemonRunInfo RunInfo { get; private set; } = null!;
	public TdcpServer Server { get; private set; } = null!;

	/// <summary>Publishes <paramref name="count"/> numbered <c>session.changed</c> notifications.</summary>
	public void Publish(int count, int first = 0)
	{
		for (var i = 0; i < count; i++)
			Bus.Publish("session.changed", new SamplePayload("n" + (first + i), null, first + i));
	}

	/// <summary>Stops the listener; open streams end. The bus, log and run id stay.</summary>
	public void StopServer() => Server.Stop();

	/// <summary>Starts a new listener over the same bus, log and run id (a network drop, not a daemon restart), optionally with another token.</summary>
	public void StartServer(string? token = null)
	{
		// The port was ours a moment ago; a parallel test may have grabbed it in between, so retry briefly before giving up.
		for (var attempt = 1; ; attempt++)
		{
			Server = new TdcpServer(_daemonClient, _dispatcherFactory(RunInfo, Log), token ?? Token, Log, RunInfo, _presence);
			try
			{
				Server.Start(Port);
				return;
			}
			catch (HttpListenerException) when (attempt < 40)
			{
				Server.Dispose();
				Thread.Sleep(50);
			}
		}
	}

	/// <summary>Stops everything and starts a new daemon run (new run id, empty log) on the same port.</summary>
	public void RestartAsNewRun()
	{
		DisposeRun();
		StartRun();
	}

	private void StartRun()
	{
		Bus     = new RemoteEventBus();
		Log     = new EventLog(Bus, capacity: _logCapacity);
		RunInfo = new DaemonRunInfo();
		StartServer();
	}

	private void DisposeRun()
	{
		Server.Dispose();
		Log.Dispose();
		Bus.Dispose();
	}

	public void Dispose() => DisposeRun();
}
