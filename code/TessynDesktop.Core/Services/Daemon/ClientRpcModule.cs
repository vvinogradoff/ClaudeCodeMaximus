using System;
using System.Reflection;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.Services;

/// <summary>Remote-callable <c>client.hello</c>: lets a client learn the protocol version and detect a daemon restart.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class ClientRpcModule : IRemoteRpcModule
{
	private readonly TimeProvider _time;
	private readonly string       _runId = Guid.NewGuid().ToString("N");
	private readonly string       _daemonVersion;

	public ClientRpcModule(TimeProvider? time = null)
	{
		_time          = time ?? TimeProvider.System;
		_daemonVersion = ReadDaemonVersion();
	}

	/// <inheritdoc />
	public void Register(TdcpMethodRegistry registry) =>
		registry.AddSync(Constants.Remote.Methods.ClientHello, _ => Hello(), remoteCallable: true);

	private HelloResult Hello() =>
		new(Constants.Remote.ProtocolVersion, _daemonVersion, Environment.MachineName, _runId, _time.GetUtcNow());

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
