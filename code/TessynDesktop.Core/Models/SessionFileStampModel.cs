using System;

namespace TessynDesktop.Models;

/// <summary>Cheap identity of a session file's content: caches keyed by a stamp are valid while the stamp is unchanged.</summary>
/// <param name="Length">File length in bytes.</param>
/// <param name="LastWriteUtc">Last write time.</param>
/// <remarks>Created by Claude</remarks>
public sealed record SessionFileStampModel(long Length, DateTimeOffset LastWriteUtc);
