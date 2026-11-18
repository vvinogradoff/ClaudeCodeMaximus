using System.IO;
using System.Windows.Media.Imaging;

namespace TessynDesktop.WPF.Services;

/// <summary><see cref="ITiffConverter"/> with the Windows Imaging Component: first frame, loaded fully, frozen, decoded and encoded off the UI thread.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class WicTiffConverter : ITiffConverter
{
	public Task<bool> ConvertToPngAsync(string tiffPath, string pngPath, CancellationToken cancellationToken) => Task.Run(() => Convert(tiffPath, pngPath), cancellationToken);

	private static bool Convert(string tiffPath, string pngPath)
	{
		try
		{
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
