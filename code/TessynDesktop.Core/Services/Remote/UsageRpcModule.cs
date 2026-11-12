using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace TessynDesktop.Services;

/// <summary>Remote <c>usage.get</c>: plan usage of one profile (contract section 3.21).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class UsageRpcModule : IRemoteRpcModule
{
	private readonly IRemoteUsageService _usage;

	public UsageRpcModule(IRemoteUsageService usage) => _usage = usage;

	/// <inheritdoc />
	public void Register(TdcpMethodRegistry registry) =>
		registry.Add(Constants.Remote.Methods.UsageGet, async (p, ct) => await GetAsync(p, ct), remoteCallable: true);

	private async Task<object?> GetAsync(JsonNode? @params, System.Threading.CancellationToken cancellationToken)
	{
		var profileId = TdcpParams.OptionalString(@params, "profileId") ?? Constants.Remote.DefaultOptionId;
		return await _usage.GetAsync(profileId, cancellationToken);
	}
}
