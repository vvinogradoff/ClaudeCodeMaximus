using System.Linq;
using System.Text.Json.Nodes;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.Services;

/// <summary>
/// <c>session.switchProfile</c>: the remote form of <c>profile.switch</c> (ISSUE.013 R1) for the web client. It runs the same
/// <see cref="IProfileSwitchService"/> and answers a <see cref="SessionProfileSwitchResult"/> without local paths; <c>profile.switch</c> itself
/// stays loopback only. The service publishes the session's settings change, so lists and open sessions refresh.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class SessionProfileRpcModule : IRemoteRpcModule
{
	private readonly IProfileSwitchService _switch;

	public SessionProfileRpcModule(IProfileSwitchService profileSwitch)
	{
		_switch = profileSwitch;
	}

	/// <inheritdoc />
	public void Register(TdcpMethodRegistry registry) =>
		registry.AddSync(Constants.Remote.Methods.SessionSwitchProfile, Switch, remoteCallable: true);

	private object? Switch(JsonNode? @params)
	{
		var nodeId      = TdcpParams.RequireString(@params, "nodeId");
		var profileId   = TdcpParams.RequireString(@params, "profileId");
		var descendants = TdcpParams.OptionalBool(@params, "includeDescendants") ?? false;

		try
		{
			// The service accepts "default", a stored Claude profile id or a ChatGPT profile id and refuses anything else.
			var result = _switch.Switch(nodeId, profileId, descendants);
			return new SessionProfileSwitchResult(
				result.ProfileId,
				result.ProfileName,
				result.NodeMoved,
				result.Moved,
				result.Queued,
				[.. result.Failed.Select(f => new SessionProfileSwitchFailureDto(f.NodeId, f.Name, f.Paths.Count))]);
		}
		catch (ProfileSwitchException ex)
		{
			throw new TdcpInvalidParamsException(ex.Message);
		}
	}
}
