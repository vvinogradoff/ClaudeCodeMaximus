namespace TessynDesktop.Models.Remote;

/// <summary>Patch of <see cref="FileAccessSettingsDto"/>: absent (or null) members are left alone.</summary>
/// <remarks>Created by Claude</remarks>
public sealed record FileAccessSettingsPatch(
	FileAccessModeDto? Mode = null,
	int? MaxRelayChunkBytes = null,
	long? MaxTextFileBytes = null,
	long? MaxImageFileBytes = null,
	long? MaxPdfFileBytes = null,
	long? MaxVideoFileBytes = null);
