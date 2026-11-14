using System;
using System.Collections.Generic;

namespace TessynDesktop.Models;

/// <summary>A page of entries read from a consistent snapshot of the index.</summary>
/// <param name="FileId">Content generation the ordinals belong to.</param>
/// <param name="Total">Entries in the file at the snapshot.</param>
/// <param name="Entries">The page, ascending.</param>
/// <param name="HasMoreBefore">Entries exist before the first one of the page.</param>
/// <param name="HasMoreAfter">Entries exist after the last one of the page.</param>
/// <param name="ContentVersion">Content version of the snapshot.</param>
/// <remarks>Created by Claude</remarks>
public sealed record SessionContentPageModel(
	Guid FileId,
	int Total,
	IReadOnlyList<SessionContentEntryModel> Entries,
	bool HasMoreBefore,
	bool HasMoreAfter,
	long ContentVersion);
