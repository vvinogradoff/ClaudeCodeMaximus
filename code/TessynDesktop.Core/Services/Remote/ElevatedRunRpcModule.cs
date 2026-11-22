using System.Linq;
using System.Text.Json.Nodes;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.Services;

/// <summary>
/// The <c>elevated.*</c> methods of tdcp-spec 10.A13: list, get, approve, launched, launchFailed, decline and stop. All are loopback only (<c>remoteCallable: false</c>):
/// a request is approved on this PC by the desktop client, never through the relay.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class ElevatedRunRpcModule(IElevatedRunService runs) : IRemoteRpcModule
{
	/// <inheritdoc />
	public void Register(TdcpMethodRegistry registry)
	{
		registry.AddSync(RemoteProtocol.ElevatedRuns.ListMethod, List, remoteCallable: false);
		registry.AddSync(RemoteProtocol.ElevatedRuns.GetMethod, Get, remoteCallable: false);
		registry.AddSync(RemoteProtocol.ElevatedRuns.ApproveMethod, p => Guard(() => new ElevatedApproveResult(runs.Approve(RequestId(p)).ToDto())), remoteCallable: false);
		registry.AddSync(RemoteProtocol.ElevatedRuns.LaunchedMethod, p => Guard(() => { runs.ReportLaunched(RequestId(p)); return new TdcpEmptyResult(); }), remoteCallable: false);
		registry.AddSync(RemoteProtocol.ElevatedRuns.LaunchFailedMethod, LaunchFailed, remoteCallable: false);
		registry.AddSync(RemoteProtocol.ElevatedRuns.DeclineMethod, p => Guard(() => { runs.Decline(RequestId(p), TdcpParams.OptionalString(p, "note")); return new TdcpEmptyResult(); }), remoteCallable: false);
		registry.AddSync(RemoteProtocol.ElevatedRuns.StopMethod, p => Guard(() => { runs.Stop(RequestId(p)); return new TdcpEmptyResult(); }), remoteCallable: false);
	}

	private object? List(JsonNode? @params)
	{
		var status = TdcpParams.OptionalString(@params, "status") ?? RemoteProtocol.ElevatedRuns.ListActive;
		if (status is not (RemoteProtocol.ElevatedRuns.ListActive or RemoteProtocol.ElevatedRuns.ListAll))
			throw new TdcpInvalidParamsException("'status' must be 'active' or 'all'");

		var found = runs.List(TdcpParams.OptionalString(@params, "nodeId"), status == RemoteProtocol.ElevatedRuns.ListAll, RemoteProtocol.ElevatedRuns.MaxListed);
		return new ElevatedListResult([.. found.Select(r => r.ToDto(runs.GetLogPath(r.Id), relay: false, runs.ShortIdOf(r.RequesterNodeId)))]);
	}

	private object? Get(JsonNode? @params)
	{
		var id    = RequestId(@params);
		var lines = TdcpParams.OptionalInt(@params, "tailLines") ?? 0;
		if (lines is < 0 or > RemoteProtocol.ElevatedRuns.MaxTailLines)
			throw new TdcpInvalidParamsException($"'tailLines' must be 0 to {RemoteProtocol.ElevatedRuns.MaxTailLines}");

		var run = runs.Find(id) ?? throw new TdcpInvalidParamsException(Constants.ElevatedRuns.UnknownRequestText);
		return new ElevatedGetResult(run.ToDto(runs.GetLogPath(id), relay: false, runs.ShortIdOf(run.RequesterNodeId)), lines == 0 ? null : runs.GetOutputTail(id, lines));
	}

	private object? LaunchFailed(JsonNode? @params) => Guard(() =>
	{
		runs.ReportLaunchFailed(RequestId(@params), TdcpParams.RequireBool(@params, "declined"), TdcpParams.OptionalString(@params, "reason"));
		return new TdcpEmptyResult();
	});

	private static string RequestId(JsonNode? @params) => TdcpParams.RequireString(@params, "requestId").Trim();

	/// <summary>An unknown request is -32602, a request in the wrong status -32000 with the user-facing text.</summary>
	private static object? Guard(System.Func<object?> action)
	{
		try
		{
			return action();
		}
		catch (ElevatedRunException ex) when (ex.IsUnknown)
		{
			throw new TdcpInvalidParamsException(ex.Message);
		}
		catch (ElevatedRunException ex)
		{
			throw new TdcpException(ex.Message);
		}
	}
}
