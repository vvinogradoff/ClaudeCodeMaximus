using TessynDesktop.Models;
using TessynDesktop.Services;

namespace Tessyn.Web.E2E.Tests.Harness;

/// <summary>Relay config held in memory instead of the DPAPI file.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class InMemoryRelayConfigStore(RelayConfigModel config) : IRelayConfigStore
{
	private RelayConfigModel? _config = config;

	public RelayConfigModel? Load() => _config;

	public void Save(RelayConfigModel value) => _config = value;

	public void Delete() => _config = null;
}
