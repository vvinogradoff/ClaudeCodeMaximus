using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.Services;

/// <summary>
/// Session attachments (decision A12): files stored under <c>%AppData%\TessynDesktop\attachments\&lt;profileKey&gt;\&lt;nodeId&gt;\</c>
/// as <c>yyyyMMdd-HHmmss-&lt;safe name&gt;</c>. Nothing is deleted automatically.
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface IAttachmentService
{
	/// <summary>Copies a local file into the attachments of the session (the desktop's file mode). Loopback only.</summary>
	AttachmentDto Import(string nodeId, string sourcePath);

	/// <summary>
	/// Downloads the blob the web server holds (<c>attachment.fetch</c>), checks size and SHA-256 and stores it. A mismatch deletes
	/// the partial file and throws <see cref="TdcpException"/>.
	/// </summary>
	Task<AttachmentDto> FetchAsync(string nodeId, string blobId, string name, long size, string sha256, CancellationToken cancellationToken);

	/// <summary>The attachments of the session, newest first.</summary>
	AttachmentListResult List(string nodeId);

	/// <summary>The session and full path behind an attachment id; false when the id is malformed or the file is gone.</summary>
	bool TryResolve(string attachmentId, out string nodeId, out string fullPath);
}
