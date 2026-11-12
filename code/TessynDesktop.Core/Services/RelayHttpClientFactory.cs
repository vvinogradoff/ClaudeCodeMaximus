using System;
using System.Net.Http;
using System.Threading;

namespace TessynDesktop.Services;

/// <summary>Default <see cref="IRelayHttpClientFactory"/>: a fresh handler per connection so a stale socket never survives a reconnect.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class RelayHttpClientFactory : IRelayHttpClientFactory
{
	public HttpClient Create() => new(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) })
	{
		Timeout = Timeout.InfiniteTimeSpan,
	};
}
