using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Models.Remote;
using TessynDesktop.Services;

namespace TessynDesktop.Tests.Codex.Engine;

/// <summary>A session's attachments (FR.31) as a fixed list, for the <c>localImage</c> input of FR.33.8.</summary>
/// <remarks>Created by Claude</remarks>
internal sealed class FakeAttachmentService : IAttachmentService
{
	public List<AttachmentDto> Attachments { get; } = [];

	public AttachmentListResult List(string nodeId) => new(nodeId, Attachments);

	public AttachmentDto Import(string nodeId, string sourcePath) => throw new NotSupportedException();

	public Task<AttachmentDto> FetchAsync(string nodeId, string blobId, string name, long size, string sha256, CancellationToken cancellationToken) =>
		throw new NotSupportedException();

	public bool IsStoredAttachment(string finalPath, string nodeId) => false;

	public bool TryResolve(string attachmentId, out string nodeId, out string fullPath) => throw new NotSupportedException();
}
