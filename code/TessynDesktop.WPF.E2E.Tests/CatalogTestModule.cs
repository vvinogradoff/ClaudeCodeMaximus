using TessynDesktop.Models.Remote;
using TessynDesktop.Services;

namespace TessynDesktop.WPF.E2E.Tests;

/// <summary>A test-only <c>catalog.get</c> that serves the optional media allowlist (the daemon does not send it yet).</summary>
/// <remarks>Created by Claude</remarks>
internal sealed class CatalogTestModule : IRemoteRpcModule
{
	private readonly IReadOnlyList<string> _mediaHosts;

	private readonly CatalogResult? _catalog;

	public CatalogTestModule(IReadOnlyList<string> mediaHosts, CatalogResult? catalog = null)
	{
		_mediaHosts = mediaHosts;
		_catalog = catalog;
	}

	public void Register(TdcpMethodRegistry registry) =>
		registry.AddSync("catalog.get", _ => (_catalog ?? new CatalogResult([], [], [])) with { MediaPreviewHosts = _mediaHosts }, remoteCallable: true);
}
