using System.Collections.Generic;

namespace TessynDesktop.Models.Remote;

/// <summary>Answer of <c>attachment.list</c>: the attachments of one session, newest first.</summary>
/// <remarks>Created by Claude</remarks>
public sealed record AttachmentListResult(string NodeId, IReadOnlyList<AttachmentDto> Attachments);
