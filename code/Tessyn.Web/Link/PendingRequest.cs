namespace Tessyn.Web.Link;

/// <remarks>Created by Claude</remarks>
public sealed class PendingRequest
{
	public PendingRequest(string id, DaemonLink link)
	{
		Id = id;
		Link = link;
	}

	public string Id { get; }
	public DaemonLink Link { get; }
	public TaskCompletionSource<RpcOutcome> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}
