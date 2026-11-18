using System.IO;
using System.Windows.Media.Imaging;

namespace TessynDesktop.WPF.Services;

/// <summary>
/// <see cref="ITiffConverter"/> with the Windows Imaging Component: first frame, loaded fully, frozen, decoded and encoded off the UI thread. The first frame's
/// pixel size is read before the full decode, and a frame over the pixel limit is refused, so a small file with huge dimensions cannot allocate gigabytes.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class WicTiffConverter : ITiffConverter
{
	private readonly long _maxPixels;

	public WicTiffConverter() : this(Constants.Limits.MaxTiffPixels)
	{
	}

	/// <summary>A converter that refuses a first frame with more than <paramref name="maxPixels"/> pixels.</summary>
	public WicTiffConverter(long maxPixels)
	{
		_maxPixels = maxPixels;
	}

	public Task<bool> ConvertToPngAsync(string tiffPath, string pngPath, CancellationToken cancellationToken) => Task.Run(() => Convert(tiffPath, pngPath), cancellationToken);

	private bool Convert(string tiffPath, string pngPath)
	{
		try
		{
			if (!WithinPixelLimit(tiffPath))
				return false;

			BitmapFrame frame;
			using (var input = new FileStream(tiffPath, FileMode.Open, FileAccess.Read, FileShare.Read))
			{
				var decoder = new TiffBitmapDecoder(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
				if (decoder.Frames.Count == 0)
					return false;

				frame = decoder.Frames[0];
				frame.Freeze();
			}

			var encoder = new PngBitmapEncoder();
			encoder.Frames.Add(frame);
			using (var output = new FileStream(pngPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
				encoder.Save(output);

			return true;
		}
		catch (Exception ex) when (ex is IOException or NotSupportedException or InvalidOperationException or ArgumentException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
		{
			TryDelete(pngPath);
			return false;
		}
	}

	/// <summary>Reads only the first frame's header (delayed creation, nothing cached): false when it is empty or has more pixels than the limit.</summary>
	private bool WithinPixelLimit(string tiffPath)
	{
		using var input = new FileStream(tiffPath, FileMode.Open, FileAccess.Read, FileShare.Read);
		var decoder = new TiffBitmapDecoder(input, BitmapCreateOptions.DelayCreation | BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.None);
		if (decoder.Frames.Count == 0)
			return false;

		var first = decoder.Frames[0];
		return (long)first.PixelWidth * first.PixelHeight <= _maxPixels;
	}

	private static void TryDelete(string path)
	{
		try
		{
			File.Delete(path);
		}
		catch (IOException)
		{
		}
		catch (UnauthorizedAccessException)
		{
		}
	}
}
