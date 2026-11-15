using System.Text.Json.Nodes;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.Services;

/// <summary>
/// <c>profile.switch</c> (ISSUE.013 R1): moves a session, and optionally its descendants, to another profile. Loopback only: it
/// moves transcripts on disk and returns local paths, so it is never remote-callable.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class ProfileRpcModule : IRemoteRpcModule
{
	private readonly IProfileSwitchService _switch;

	public ProfileRpcModule(IProfileSwitchService profileSwitch)
	{
		_switch = profileSwitch;
	}

	/// <inheritdoc />
	public void Register(TdcpMethodRegistry registry) =>
		registry.AddSync(RemoteProtocol.LoopbackMethods.ProfileSwitch, Switch, remoteCallable: false);

	private object? Switch(JsonNode? @params)
	{
		var nodeId      = TdcpParams.RequireString(@params, "nodeId");
		var profileId   = TdcpParams.RequireString(@params, "profileId");
		var descendants = TdcpParams.OptionalBool(@params, "includeDescendants") ?? false;

		try
		{
			return _switch.Switch(nodeId, profileId, descendants);
		}
		catch (ProfileSwitchException ex)
		{
			throw new TdcpInvalidParamsException(ex.Message);
		}
	}
}
