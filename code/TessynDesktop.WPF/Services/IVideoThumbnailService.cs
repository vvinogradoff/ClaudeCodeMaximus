namespace TessynDesktop.WPF.Services;

/// <summary>Makes the first-frame picture of a video file (FR.26.6).</summary>
/// <remarks>Created by Claude</remarks>
public interface IVideoThumbnailService
{
	/// <summary>
	/// Writes a PNG of the video's first frame to <paramref name="pngPath"/> (at most <paramref name="maxPixels"/> on the longer side); false when
	/// no frame could be made (no codec for the file, a damaged file, a timeout). Never throws for a bad file.
	/// </summary>
	Task<bool> TryCreateAsync(string videoPath, string pngPath, int maxPixels, CancellationToken cancellationToken);
}
