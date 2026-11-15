using System;

namespace TessynDesktop.Models.Remote;

/// <summary>An attachment stored by the daemon (<c>attachment.import</c>, <c>attachment.fetch</c>, <c>attachment.list</c>).</summary>
/// <param name="AttachmentId">Opaque handle: <c>file.read</c> accepts it, also over the relay. Stable across restarts.</param>
/// <param name="Path">Absolute path on the daemon's machine; the text a client puts into the prompt.</param>
/// <param name="Name">File name without the timestamp prefix, as sanitised for the file system.</param>
/// <param name="Size">File size in bytes.</param>
/// <param name="Mime">MIME type from the extension.</param>
/// <param name="CreatedUtc">When the attachment was stored.</param>
/// <remarks>Created by Claude</remarks>
public sealed record AttachmentDto(string AttachmentId, string Path, string Name, long Size, string Mime, DateTimeOffset CreatedUtc);
