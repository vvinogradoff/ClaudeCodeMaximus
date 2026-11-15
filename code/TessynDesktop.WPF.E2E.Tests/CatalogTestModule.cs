using TessynDesktop.Models.Remote;
using TessynDesktop.Services;

namespace TessynDesktop.WPF.E2E.Tests;

/// <summary>A test-only <c>catalog.get</c> that serves the optional media allowlist (the daemon does not send it yet).</summary>
/// <remarks>Created by Claude</remarks>
internal sealed class CatalogTestModule : IRemoteRpcModule
{
	private readonly IReadOnlyList<string> _mediaHosts;

	public CatalogTestModule(IReadOnlyList<string> mediaHosts)
	{
		_mediaHosts = mediaHosts;
	}

	public void Register(TdcpMethodRegistry registry) =>
		registry.AddSync("catalog.get", _ => new CatalogResult([], [], []) { MediaPreviewHosts = _mediaHosts }, remoteCallable: true);
}
