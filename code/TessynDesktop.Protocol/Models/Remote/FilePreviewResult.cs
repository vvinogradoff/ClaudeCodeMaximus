using System;

namespace TessynDesktop.Models.Remote;

/// <summary>The handle <c>file.preview</c> issues for a path that occurs in an entry's text.</summary>
/// <param name="Handle">Opaque handle for <c>file.read</c>; valid for a limited time.</param>
/// <param name="Name">File name.</param>
/// <param name="Mime">MIME type from the extension.</param>
/// <param name="Size">File size in bytes.</param>
/// <param name="MtimeUtc">Last write time.</param>
/// <remarks>Created by Claude</remarks>
public sealed record FilePreviewResult(string Handle, string Name, string Mime, long Size, DateTimeOffset MtimeUtc);
