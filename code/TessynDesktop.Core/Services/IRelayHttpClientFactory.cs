using System.Net.Http;

namespace TessynDesktop.Services;

/// <summary>Creates the HttpClient the relay connector uses for one connection (it disposes the client afterwards). Lets tests inject a handler.</summary>
/// <remarks>Created by Claude</remarks>
public interface IRelayHttpClientFactory
{
	/// <summary>A new client with an infinite <see cref="HttpClient.Timeout"/> (the SSE stream is long-lived).</summary>
	HttpClient Create();
}
