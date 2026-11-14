using System;

namespace TessynDesktop.Models.Remote;

/// <summary>O(1) content header of one session (P1.G): everything the daemon knows from the entry index without reading entries.</summary>
/// <param name="NodeId">The session's NodeId.</param>
/// <param name="FileId">Content generation of the transcript file; omitted when the file does not exist yet or there is no content store.</param>
/// <param name="Epoch">Transcript epoch (see <c>session.get</c>).</param>
/// <param name="EntryCount">Number of entries in the file.</param>
/// <param name="Seq">Entries ever appended to <paramref name="FileId"/>.</param>
/// <param name="FirstTimestamp">Timestamp of the first entry; omitted for an empty file.</param>
/// <param name="LastTimestamp">Timestamp of the last entry; omitted for an empty file.</param>
/// <param name="LastUsed">Settings the session was last used with (as in <c>session.get</c>).</param>
/// <param name="ContentVersion">Increases with every change of the index.</param>
/// <param name="FileLength">Bytes of the transcript file covered by the index.</param>
/// <remarks>Created by Claude</remarks>
public sealed record SessionHeaderDto(
	string NodeId,
	string? FileId,
	int Epoch,
	int EntryCount,
	long Seq,
	DateTimeOffset? FirstTimestamp,
	DateTimeOffset? LastTimestamp,
	LastUsedSettingsDto LastUsed,
	long ContentVersion,
	long FileLength);
