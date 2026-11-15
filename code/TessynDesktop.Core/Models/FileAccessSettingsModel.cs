namespace TessynDesktop.Models;

/// <summary>Policy of the remote file reads (<c>AppSettings.FileAccess</c>). Loopback clients are not limited by the type list and the size caps.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class FileAccessSettingsModel
{
	/// <summary>Decision A11 (option 2): referenced files, attachments and the files inside the session's working directory.</summary>
	public FileAccessMode Mode { get; set; } = FileAccessMode.WorkingDirectory;

	/// <summary>Most bytes one relay <c>file.read</c> call returns (before base64).</summary>
	public int MaxRelayChunkBytes { get; set; } = Constants.FileAccess.DefaultRelayChunkBytes;

	/// <summary>Largest text, code or json file a relay client may read.</summary>
	public long MaxTextFileBytes { get; set; } = Constants.FileAccess.DefaultMaxTextFileBytes;

	public long MaxImageFileBytes { get; set; } = Constants.FileAccess.DefaultMaxImageFileBytes;
	public long MaxPdfFileBytes { get; set; } = Constants.FileAccess.DefaultMaxPdfFileBytes;
	public long MaxVideoFileBytes { get; set; } = Constants.FileAccess.DefaultMaxVideoFileBytes;
}
