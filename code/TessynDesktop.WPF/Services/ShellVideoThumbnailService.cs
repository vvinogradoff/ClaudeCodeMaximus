using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Logging;

namespace TessynDesktop.WPF.Services;

/// <summary>
/// <see cref="IVideoThumbnailService"/> over the Windows Shell thumbnail API (<c>IShellItemImageFactory</c>): the frame comes from the codecs
/// and thumbnail handlers installed on the machine, offline, with no extra program. Only a real thumbnail is accepted (never the file-type icon the
/// shell falls back to). The call runs on its own STA thread with a time limit, since a shell handler can hang on a damaged file; the thread is
/// abandoned when it does.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class ShellVideoThumbnailService : IVideoThumbnailService
{
	private static readonly TimeSpan Limit = TimeSpan.FromSeconds(20);

	private readonly ILogger<ShellVideoThumbnailService> _logger;

	public ShellVideoThumbnailService(ILogger<ShellVideoThumbnailService> logger)
	{
		_logger = logger;
	}

	public async Task<bool> TryCreateAsync(string videoPath, string pngPath, int maxPixels, CancellationToken cancellationToken)
	{
		var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var thread = new Thread(() =>
		{
			try
			{
				done.TrySetResult(Create(videoPath, pngPath, maxPixels));
			}
			catch (Exception ex)
			{
				_logger.LogInformation(ex, "The shell could not make a video frame");
				done.TrySetResult(false);
			}
		}) { IsBackground = true, Name = "VideoThumbnail" };
		thread.SetApartmentState(ApartmentState.STA);
		thread.Start();

		try
		{
			return await done.Task.WaitAsync(Limit, cancellationToken);
		}
		catch (TimeoutException)
		{
			_logger.LogInformation("The shell took too long to make a video frame");
			return false;
		}
	}

	private static bool Create(string videoPath, string pngPath, int maxPixels)
	{
		var iid = typeof(IShellItemImageFactory).GUID;
		if (SHCreateItemFromParsingName(Path.GetFullPath(videoPath), IntPtr.Zero, ref iid, out var item) != 0 || item == null)
			return false;

		IntPtr bitmap = IntPtr.Zero;
		try
		{
			var size = new SIZE { cx = maxPixels, cy = maxPixels };
			if (item.GetImage(size, SIIGBF.BiggerSizeOk | SIIGBF.ThumbnailOnly, out bitmap) != 0 || bitmap == IntPtr.Zero)
				return false;

			var source = Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
			var opaque = new FormatConvertedBitmap(source, System.Windows.Media.PixelFormats.Bgr32, null, 0);   // a video frame has no transparency; the HBITMAP alpha is not reliable
			opaque.Freeze();
			if (opaque.PixelWidth < 2 || opaque.PixelHeight < 2)
				return false;

			var encoder = new PngBitmapEncoder();
			encoder.Frames.Add(BitmapFrame.Create(opaque));
			using var file = new FileStream(pngPath, FileMode.Create, FileAccess.Write, FileShare.Read);
			encoder.Save(file);
			return true;
		}
		finally
		{
			if (bitmap != IntPtr.Zero)
				DeleteObject(bitmap);
			Marshal.ReleaseComObject(item);
		}
	}

	[DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
	private static extern int SHCreateItemFromParsingName(string path, IntPtr bindContext, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory? item);

	[DllImport("gdi32.dll")]
	private static extern bool DeleteObject(IntPtr handle);

	[StructLayout(LayoutKind.Sequential)]
	private struct SIZE
	{
		public int cx;
		public int cy;
	}

	[Flags]
	private enum SIIGBF
	{
		BiggerSizeOk = 0x1,
		ThumbnailOnly = 0x8,
	}

	[ComImport]
	[Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	private interface IShellItemImageFactory
	{
		[PreserveSig]
		int GetImage(SIZE size, SIIGBF flags, out IntPtr phbm);
	}
}
