using System.Text.Json.Nodes;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.Services;

/// <summary>
/// Remote methods that read files by handle: <c>file.read</c> and <c>file.preview</c> (Focus references, step 1). Both are
/// remote-callable; the policy for the relay transport (<c>AppSettings.FileAccess</c>) is applied by <see cref="IFileAccessService"/>.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class FileRpcModule : IRemoteRpcModule
{
	private readonly IFileAccessService _files;

	public FileRpcModule(IFileAccessService files)
	{
		_files = files;
	}

	/// <inheritdoc />
	public void Register(TdcpMethodRegistry registry)
	{
		registry.AddSync(Constants.Remote.Methods.FileRead, Read, remoteCallable: true);
		registry.AddSync(Constants.Remote.Methods.FilePreview, Preview, remoteCallable: true);
	}

	private object? Read(JsonNode? @params)
	{
		var handle = TdcpParams.RequireString(@params, "handle");
		var offset = TdcpParams.OptionalLong(@params, "offset");
		var length = TdcpParams.OptionalInt(@params, "length");

		return _files.Read(handle, offset, length, TdcpCallContext.Transport);
	}

	private object? Preview(JsonNode? @params)
	{
		var nodeId  = TdcpParams.RequireString(@params, "nodeId");
		var entryId = TdcpParams.OptionalString(@params, "entryId");
		var itemId  = TdcpParams.OptionalString(@params, "itemId");
		var path    = TdcpParams.RequireString(@params, "path");

		// Exactly one source of text: an entry of the transcript or a Focus item (tdcp-spec 10.A11).
		if ((entryId == null) == (itemId == null))
			throw new TdcpInvalidParamsException("Give exactly one of 'entryId' and 'itemId'");

		return itemId != null
			? _files.PreviewForItem(nodeId, itemId, path, TdcpCallContext.Transport)
			: _files.Preview(nodeId, entryId!, path, TdcpCallContext.Transport);
	}
}
