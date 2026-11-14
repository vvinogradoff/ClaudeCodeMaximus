using System;

namespace TessynDesktop.Models;

/// <summary>O(1) summary of a session file's index: no entry content is read to produce it.</summary>
/// <param name="Exists">False when the session file is missing; every other member is then empty.</param>
/// <param name="FileId">Identity of the file's content generation. A rewrite, compaction or repair assigns a new one.</param>
/// <param name="EntryCount">Entries in the file (empty ones included), equal to the dense index of the next entry.</param>
/// <param name="NextOrdinal">Ordinal the next appended entry receives; ordinals are never reused within a <paramref name="FileId"/>.</param>
/// <param name="FirstTimestamp">Timestamp of the first entry, null for an empty file.</param>
/// <param name="LastTimestamp">Timestamp of the last entry, null for an empty file.</param>
/// <param name="LastProfileName">Profile of the last entry that carried one.</param>
/// <param name="LastModelId">Model of the last entry that carried one.</param>
/// <param name="LastEffort">Effort of the last entry that carried one.</param>
/// <param name="ContentVersion">Increases with every change of the index (append, rebuild); equal values mean equal content.</param>
/// <param name="FileLength">Bytes of the file covered by the index.</param>
/// <remarks>Created by Claude</remarks>
public sealed record SessionContentHeaderModel(
	bool Exists,
	Guid FileId,
	int EntryCount,
	int NextOrdinal,
	DateTimeOffset? FirstTimestamp,
	DateTimeOffset? LastTimestamp,
	string? LastProfileName,
	string? LastModelId,
	string? LastEffort,
	long ContentVersion,
	long FileLength);
