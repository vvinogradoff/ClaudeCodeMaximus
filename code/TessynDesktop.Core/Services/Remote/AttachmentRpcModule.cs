using System.Text.Json.Nodes;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.Services;

/// <summary>
/// Attachment methods. <c>attachment.import</c> is loopback-only (it reads a path on this machine). <c>attachment.list</c> is remote.
/// <c>attachment.fetch</c> is remote too, but only the web server calls it (it is not on the browsers' method list): it pulls an upload
/// from the server and stores it.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class AttachmentRpcModule : IRemoteRpcModule
{
	private readonly IAttachmentService _attachments;

	public AttachmentRpcModule(IAttachmentService attachments)
	{
		_attachments = attachments;
	}

	/// <inheritdoc />
	public void Register(TdcpMethodRegistry registry)
	{
		registry.AddSync(RemoteProtocol.LoopbackMethods.AttachmentImport, Import, remoteCallable: false);
		registry.AddSync(Constants.Remote.Methods.AttachmentList, List, remoteCallable: true);
		registry.Add(Constants.Remote.Methods.AttachmentFetch, async (p, ct) => await FetchAsync(p, ct), remoteCallable: true);
	}

	private object? Import(JsonNode? @params)
	{
		var nodeId = TdcpParams.RequireString(@params, "nodeId");
		var source = TdcpParams.RequireString(@params, "sourcePath");
		return _attachments.Import(nodeId, source);
	}

	private object? List(JsonNode? @params) => _attachments.List(TdcpParams.RequireString(@params, "nodeId"));

	private async System.Threading.Tasks.Task<object?> FetchAsync(JsonNode? @params, System.Threading.CancellationToken ct)
	{
		var blobId = TdcpParams.RequireString(@params, "blobId");
		var nodeId = TdcpParams.RequireString(@params, "nodeId");
		var name   = TdcpParams.RequireString(@params, "name");
		var size   = TdcpParams.OptionalLong(@params, "size") ?? throw new TdcpInvalidParamsException("'size' is required");
		var sha256 = TdcpParams.RequireString(@params, "sha256");

		return await _attachments.FetchAsync(nodeId, blobId, name, size, sha256, ct);
	}
}
