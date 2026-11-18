namespace TessynDesktop.WPF.Services;

/// <summary>Turns a TIFF image into a PNG, because the preview page cannot show TIFF (plan D9).</summary>
/// <remarks>Created by Claude</remarks>
public interface ITiffConverter
{
	/// <summary>Decodes the first frame of <paramref name="tiffPath"/> and writes it as a PNG to <paramref name="pngPath"/>; false when the file cannot be decoded (nothing is left at <paramref name="pngPath"/>).</summary>
	Task<bool> ConvertToPngAsync(string tiffPath, string pngPath, CancellationToken cancellationToken);
}
