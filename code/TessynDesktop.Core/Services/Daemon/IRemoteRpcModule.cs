namespace TessynDesktop.Services;

/// <summary>
/// A feature module that contributes JSON-RPC methods to the daemon. Register one implementation per feature with
/// DI (<c>services.AddSingleton&lt;IRemoteRpcModule, MyModule&gt;()</c>); the <see cref="TdcpDispatcher"/> collects them all.
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface IRemoteRpcModule
{
	/// <summary>Adds the module's methods to <paramref name="registry"/>.</summary>
	void Register(TdcpMethodRegistry registry);
}
