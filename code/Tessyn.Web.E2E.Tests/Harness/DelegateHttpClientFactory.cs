using TessynDesktop.Services;

namespace Tessyn.Web.E2E.Tests.Harness;

/// <summary>Hands the connector an HttpClient built by a delegate (the in-memory test server handler, or a real socket client).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class DelegateHttpClientFactory(Func<HttpClient> create) : IRelayHttpClientFactory
{
	public HttpClient Create() => create();
}
