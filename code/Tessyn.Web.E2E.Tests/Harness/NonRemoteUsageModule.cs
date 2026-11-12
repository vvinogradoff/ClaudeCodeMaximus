using TessynDesktop.Services;

namespace Tessyn.Web.E2E.Tests.Harness;

/// <summary>Registers <c>usage.get</c> (which the web server allowlists) as NOT remote-callable, to prove the daemon enforces its own allowlist.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class NonRemoteUsageModule : IRemoteRpcModule
{
	public void Register(TdcpMethodRegistry registry) =>
		registry.AddSync(TessynDesktop.Constants.Remote.Methods.UsageGet, _ => new { secret = "must never be returned" }, remoteCallable: false);
}
