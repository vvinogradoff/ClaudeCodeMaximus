namespace TessynDesktop.Services;

/// <summary>Node lookup shared by the remote session modules.</summary>
/// <remarks>Created by Claude</remarks>
public static class RemoteSessionLookup
{
	/// <summary>The live model of session <paramref name="nodeId"/>; throws <see cref="TdcpException"/> ("Session not found") when there is none.</summary>
	public static Models.SessionNodeModel Require(ISessionRegistry registry, string nodeId) =>
		registry.FindModelByNodeId(nodeId) ?? throw new TdcpException("Session not found");
}
