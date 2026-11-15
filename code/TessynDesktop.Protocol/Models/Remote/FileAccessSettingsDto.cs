namespace TessynDesktop.Models.Remote;

/// <summary>The remote file-read policy inside the <c>general</c> section.</summary>
/// <remarks>Created by Claude</remarks>
public sealed record FileAccessSettingsDto(
	FileAccessModeDto Mode,
	int MaxRelayChunkBytes,
	long MaxTextFileBytes,
	long MaxImageFileBytes,
	long MaxPdfFileBytes,
	long MaxVideoFileBytes);
