using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.Tests.Fakes;

namespace TessynDesktop.WPF.Tests;

/// <summary>TIFF previews (plan D9): classified as images, decoded by the host and shown as a PNG of the same size from the preview cache.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class TiffPreviewTests : IDisposable
{
	private readonly FakeTessynClient _client = new();
	private readonly TempAppPaths _temp = new();

	public void Dispose() => _temp.Dispose();

	private FilePreviewLoader Loader(ITiffConverter? converter = null) =>
		new(_client, new FileKindClassifier(), new PreviewFileStore(_temp.Paths), new ExternalOpenPolicy(), converter ?? new WicTiffConverter());

	private static byte[] MakeTiff(int width, int height)
	{
		var pixels = new byte[width * height * 4];
		for (var i = 0; i < pixels.Length; i += 4)
		{
			pixels[i] = 200;
			pixels[i + 1] = 100;
			pixels[i + 2] = 50;
			pixels[i + 3] = 255;
		}

		var encoder = new TiffBitmapEncoder();
		encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4)));
		using var stream = new MemoryStream();
		encoder.Save(stream);
		return stream.ToArray();
	}

	private IEnumerable<string> CacheFiles() =>
		Directory.Exists(_temp.Paths.PreviewCacheDirectory) ? Directory.EnumerateFiles(_temp.Paths.PreviewCacheDirectory, "*", SearchOption.AllDirectories) : [];

	[Theory]
	[InlineData("scan.tif")]
	[InlineData("SCAN.TIFF")]
	[InlineData("C:\\pics\\scan.tiff")]
	public void TiffFiles_AreImages(string name)
	{
		Assert.Equal(PreviewKind.Image, new FileKindClassifier().Classify(name, null));
	}

	[Theory]
	[InlineData(".tif")]
	[InlineData(".tiff")]
	public void TiffFiles_MayBeOpenedInTheDefaultApp(string extension)
	{
		Assert.Contains(extension, Constants.FileTypes.ExternallyOpenable);
		Assert.True(new ExternalOpenPolicy().IsAllowed("scan" + extension));
	}

	[Fact]
	public async Task ARealTiff_BecomesAPngOfTheSameSize_AndTheTiffCopyIsGone()
	{
		_client.Files["h"] = ("scan.tif", "image/tiff", MakeTiff(7, 5));

		var loaded = await Loader().LoadAsync("h", saveToFile: false, null, CancellationToken.None);

		Assert.Null(loaded.Refusal);
		Assert.Equal(PreviewKind.Image, loaded.Kind);
		Assert.Equal("scan.tif", loaded.Name);
		Assert.EndsWith(".png", loaded.FilePath, StringComparison.Ordinal);
		var png = Assert.Single(CacheFiles());
		Assert.Equal(loaded.FilePath, png);
		using var stream = File.OpenRead(png);
		var frame = new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
		Assert.Equal((7, 5), (frame.PixelWidth, frame.PixelHeight));
	}

	[Fact]
	public async Task ADamagedTiff_IsRefused_AndLeavesNothingInTheCache()
	{
		_client.Files["h"] = ("scan.tiff", "image/tiff", [1, 2, 3, 4, 5, 6, 7, 8]);

		var loaded = await Loader().LoadAsync("h", saveToFile: false, null, CancellationToken.None);

		Assert.Equal("This TIFF image could not be decoded.", loaded.Refusal);
		Assert.Null(loaded.FilePath);
		Assert.Empty(CacheFiles());
	}

	[Fact]
	public async Task ATiffOverTheImageLimit_IsRefused_BeforeAnythingIsRead()
	{
		_client.Files["h"] = ("scan.tif", "image/tiff", new byte[Constants.Limits.MaxPreviewImageBytes + 1]);

		var loaded = await Loader().LoadAsync("h", saveToFile: false, null, CancellationToken.None);

		Assert.NotNull(loaded.Refusal);
		Assert.Contains("too large", loaded.Refusal, StringComparison.OrdinalIgnoreCase);
		Assert.Empty(CacheFiles());
	}

	[Fact]
	public async Task ATiffOverThePixelLimit_IsRefusedBeforeTheFullDecode_AndOneAtTheLimitIsConverted()
	{
		_client.Files["h"] = ("scan.tif", "image/tiff", MakeTiff(8, 5));   // 40 pixels

		var over = await Loader(new WicTiffConverter(maxPixels: 39)).LoadAsync("h", saveToFile: false, null, CancellationToken.None);

		Assert.Equal("This TIFF image could not be decoded.", over.Refusal);
		Assert.Null(over.FilePath);
		Assert.Empty(CacheFiles());

		var atLimit = await Loader(new WicTiffConverter(maxPixels: 40)).LoadAsync("h", saveToFile: false, null, CancellationToken.None);

		Assert.Null(atLimit.Refusal);
		Assert.NotNull(atLimit.FilePath);
	}

	[Fact]
	public void TheDefaultPixelLimit_IsOneHundredMegapixels()
	{
		Assert.Equal(100_000_000L, Constants.Limits.MaxTiffPixels);
	}

	[Fact]
	public async Task WithoutAConverter_ATiffIsRefused()
	{
		_client.Files["h"] = ("scan.tif", "image/tiff", MakeTiff(2, 2));
		var loader = new FilePreviewLoader(_client, new FileKindClassifier(), new PreviewFileStore(_temp.Paths), new ExternalOpenPolicy());

		var loaded = await loader.LoadAsync("h", saveToFile: false, null, CancellationToken.None);

		Assert.Equal("This TIFF image could not be decoded.", loaded.Refusal);
		Assert.Empty(CacheFiles());
	}
}
