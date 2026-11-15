using TessynDesktop.Models.Remote;

namespace TessynDesktop.Services;

/// <summary>
/// Handle-based file reads for clients (Focus references, step 1): a client never sends a raw path to read. A handle is the
/// <c>refId</c> of a file reference of a Focus item, or one issued by <see cref="Preview"/> for a path that occurs in a transcript entry.
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface IFileAccessService
{
	/// <summary>
	/// Issues a handle for <paramref name="path"/>, which must occur in the text of entry <paramref name="entryId"/> of the session
	/// (verbatim, or, when relative, as written). Throws <see cref="TdcpException"/> when it does not, the file does not exist or the
	/// policy for <paramref name="transport"/> refuses it.
	/// </summary>
	FilePreviewResult Preview(string nodeId, string entryId, string path, TdcpTransport transport);

	/// <summary>
	/// One chunk of the file behind <paramref name="handle"/>. Text types come back as UTF-8, everything else as base64; the chunk is
	/// capped per call. Throws <see cref="TdcpException"/> for an unknown handle or a refused file.
	/// </summary>
	FileReadResult Read(string handle, long? offset, int? length, TdcpTransport transport);
}
