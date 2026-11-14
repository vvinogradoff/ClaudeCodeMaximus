using System;

namespace TessynDesktop.Services;

/// <summary>A header line the scanner found, with its metadata.</summary>
/// <remarks>Created by Claude</remarks>
internal sealed record ScannedHeader(
	long Offset,
	DateTimeOffset Timestamp,
	string Role,
	string? ProfileName,
	string? ModelId,
	string? Effort);
