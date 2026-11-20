using System.Text;
using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.Tests.Fakes;

namespace TessynDesktop.WPF.Tests;

/// <summary>Reading a file behind a handle: chunks, progress, cancel, size limits, and what may reach the disk.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class FilePreviewLoaderTests : IDisposable
{
	private readonly FakeTessynClient _client = new();
	private readonly TempAppPaths _temp = new();

	private FilePreviewLoader Loader() =>
		new(_client, new FileKindClassifier(), new PreviewFileStore(_temp.Paths), new ExternalOpenPolicy());

	private sealed class Collector : IProgress<PreviewProgress>
	{
		public List<PreviewProgress> Reports { get; } = [];

		public void Report(PreviewProgress value) => Reports.Add(value);
	}

	public void Dispose() => _temp.Dispose();

	[Fact]
	public async Task Text_IsReadInChunks_WithProgress()
	{
		var text = string.Concat(Enumerable.Repeat("line of text\n", 100_000)); // 1.3 MB: three chunks of 512 KB
		_client.Files["h"] = ("notes.txt", "text/plain", Encoding.UTF8.GetBytes(text));
		var progress = new Collector();

		var loaded = await Loader().LoadAsync("h", saveToFile: false, progress, CancellationToken.None);

		Assert.Equal(PreviewKind.Text, loaded.Kind);
		Assert.Equal(text, loaded.Text);
		Assert.False(loaded.Truncated);
		Assert.Null(loaded.Refusal);
		Assert.Equal(3, _client.Calls.Count(c => c.StartsWith("file.read:h:", StringComparison.Ordinal)));
		Assert.Contains($"file.read:h:0:{Constants.Limits.PreviewChunkBytes}", _client.Calls);
		Assert.Equal(1.0, progress.Reports[^1].Fraction, 3);
		Assert.True(progress.Reports.Zip(progress.Reports.Skip(1)).All(p => p.Second.BytesRead >= p.First.BytesRead));
	}

	[Fact]
	public async Task LargeText_IsCutAtTheLimit_AndSaysSo()
	{
		var bytes = new byte[(int)Constants.Limits.MaxPreviewTextBytes + 1_000_000];
		Array.Fill(bytes, (byte)'x');
		_client.Files["h"] = ("big.log", "text/plain", bytes);

		var loaded = await Loader().LoadAsync("h", saveToFile: false, null, CancellationToken.None);

		Assert.True(loaded.Truncated);
		Assert.Equal(Constants.Limits.MaxPreviewTextBytes, loaded.Text!.Length);
		Assert.True(_client.Calls.Count(c => c.StartsWith("file.read:", StringComparison.Ordinal)) <= 5);
	}

	[Fact]
	public async Task Image_IsWrittenToTheCache_NotHeldInMemory()
	{
		var bytes = Enumerable.Range(0, 1_200_000).Select(i => (byte)(i % 251)).ToArray();
		_client.Files["h"] = ("pic.png", "image/png", bytes);

		var loaded = await Loader().LoadAsync("h", saveToFile: false, null, CancellationToken.None);

		Assert.Equal(PreviewKind.Image, loaded.Kind);
		Assert.Null(loaded.Text);
		Assert.NotNull(loaded.FilePath);
		Assert.StartsWith(_temp.Paths.PreviewCacheDirectory, loaded.FilePath, StringComparison.OrdinalIgnoreCase);
		Assert.Equal(bytes, await File.ReadAllBytesAsync(loaded.FilePath));
	}

	[Fact]
	public async Task ImageOverTheLimit_IsRefusedAfterTheFirstChunk_AndNothingIsWritten()
	{
		_client.Files["h"] = ("huge.png", "image/png", new byte[(int)Constants.Limits.MaxPreviewImageBytes + 1]);

		var loaded = await Loader().LoadAsync("h", saveToFile: false, null, CancellationToken.None);

		Assert.NotNull(loaded.Refusal);
		Assert.Contains(Constants.Texts.PreviewTooLarge, loaded.Refusal);
		Assert.Null(loaded.FilePath);
		Assert.Single(_client.Calls, c => c.StartsWith("file.read:h:", StringComparison.Ordinal));
		Assert.False(Directory.Exists(_temp.Paths.PreviewCacheDirectory) && Directory.EnumerateFiles(_temp.Paths.PreviewCacheDirectory, "*", SearchOption.AllDirectories).Any());
	}

	[Fact]
	public async Task UnknownTypes_AreNotRead()
	{
		_client.Files["h"] = ("setup.exe", "application/octet-stream", new byte[2_000_000]);

		var loaded = await Loader().LoadAsync("h", saveToFile: false, null, CancellationToken.None);

		Assert.Equal(Constants.Texts.PreviewUnsupported, loaded.Refusal);
		Assert.Single(_client.Calls, c => c.StartsWith("file.read:h:", StringComparison.Ordinal));
	}

	[Fact]
	public async Task Pdf_IsNotDownloadedForThePreview_ButIsForOpeningExternally()
	{
		_client.Files["h"] = ("doc.pdf", "application/pdf", new byte[1_300_000]);

		var preview = await Loader().LoadAsync("h", saveToFile: false, null, CancellationToken.None);
		Assert.Equal(PreviewKind.Pdf, preview.Kind);
		Assert.Null(preview.FilePath);
		Assert.Null(preview.Refusal);
		Assert.Single(_client.Calls, c => c.StartsWith("file.read:h:", StringComparison.Ordinal));

		var saved = await Loader().LoadAsync("h", saveToFile: true, null, CancellationToken.None);
		Assert.NotNull(saved.FilePath);
		Assert.Equal(1_300_000, new FileInfo(saved.FilePath).Length);
	}

	[Theory]
	[InlineData("run.ps1", "text/plain")]
	[InlineData("setup.exe", "application/octet-stream")]
	[InlineData("run.bat", "text/plain")]
	[InlineData("drawing.svg", "image/svg+xml")]
	[InlineData("code.js", "text/javascript")]
	public async Task SavingForExternalOpen_IsRefusedForTypesOffTheAllowlist_AndNothingReachesTheDisk(string name, string mime)
	{
		_client.Files["h"] = (name, mime, Encoding.UTF8.GetBytes("payload"));

		var loaded = await Loader().LoadAsync("h", saveToFile: true, null, CancellationToken.None);

		Assert.NotNull(loaded.Refusal);
		Assert.Null(loaded.FilePath);
		Assert.False(Directory.Exists(_temp.Paths.PreviewCacheDirectory) && Directory.EnumerateFiles(_temp.Paths.PreviewCacheDirectory, "*", SearchOption.AllDirectories).Any());
	}

	[Fact]
	public async Task Cancel_StopsTheReadAndLeavesNoFile()
	{
		_client.Files["h"] = ("movie.mp4", "video/mp4", new byte[3_000_000]);
		_client.FileReadGate = new TaskCompletionSource();
		using var cts = new CancellationTokenSource();

		var load = Loader().LoadAsync("h", saveToFile: false, null, cts.Token);
		await Task.Delay(50);
		cts.Cancel();

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => load);
		Assert.False(Directory.Exists(_temp.Paths.PreviewCacheDirectory) && Directory.EnumerateFiles(_temp.Paths.PreviewCacheDirectory, "*", SearchOption.AllDirectories).Any());
	}

	[Fact]
	public void TheCacheNamesFilesSafely_AndCleansUp()
	{
		var store = new PreviewFileStore(_temp.Paths);

		var path = store.NewFilePath(".PNG");
		File.WriteAllText(path, "x");

		Assert.StartsWith(Path.GetFullPath(_temp.Paths.PreviewCacheDirectory), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase);
		Assert.Equal("content.png", Path.GetFileName(path));          // a fixed name: no name from outside reaches the disk
		Assert.StartsWith("https://preview.local/", store.UrlOf(path));

		store.Delete(path);
		Assert.False(File.Exists(path));

		File.WriteAllText(store.NewFilePath(".md"), "x");
		store.Clean();
		Assert.False(Directory.Exists(_temp.Paths.PreviewCacheDirectory));
	}

	[Theory]
	[InlineData(".exe")]
	[InlineData(".ps1")]
	[InlineData("")]
	[InlineData("png")]
	[InlineData(@".png\..\..\x")]
	[InlineData(".pÿg")]
	public void TheCacheRefusesAnyExtensionOffTheAllowlist(string extension) =>
		Assert.Throws<ArgumentException>(() => new PreviewFileStore(_temp.Paths).NewFilePath(extension));

	[Theory]
	[InlineData("NUL.png")]
	[InlineData("com1.pdf")]
	[InlineData("AUX.txt")]
	[InlineData("con.mp4")]
	[InlineData("LPT1.md")]
	public async Task ReservedDeviceNamesNeverReachTheFileSystem_TheFileIsCalledContent(string name)
	{
		_client.Files["h"] = (name, name.EndsWith(".png", StringComparison.Ordinal) ? "image/png" : name.EndsWith(".mp4", StringComparison.Ordinal) ? "video/mp4" : "application/pdf", new byte[] { 1, 2, 3 });

		var loaded = await Loader().LoadAsync("h", saveToFile: true, null, CancellationToken.None);

		Assert.Null(loaded.Refusal);
		Assert.StartsWith("content.", Path.GetFileName(loaded.FilePath));
		Assert.Equal(name, loaded.Name);                                  // the display name is kept separately
	}

	private static FileReadResult Chunk(string name, string mime, long size, long offset, byte[] data, bool eof, int? lengthOverride = null) =>
		new(name, mime, size, DateTimeOffset.UnixEpoch, offset, lengthOverride ?? data.Length, "base64", Convert.ToBase64String(data), eof);

	[Fact]
	public async Task ADaemonThatLiesAboutTheSize_AndNeverEndsTheFile_IsStopped_AndTheFileIsDeleted()
	{
		// Declares 1 KB, then streams one-megabyte chunks without ever setting Eof.
		var megabyte = new byte[1024 * 1024];
		_client.FileReadHandler = (_, offset, _) => Chunk("pic.png", "image/png", 1024, offset, offset == 0 ? new byte[512] : megabyte, eof: false);

		var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Loader().LoadAsync("h", saveToFile: false, null, CancellationToken.None));

		Assert.Contains("more data than", ex.Message);
		Assert.False(Directory.Exists(_temp.Paths.PreviewCacheDirectory) && Directory.EnumerateFiles(_temp.Paths.PreviewCacheDirectory, "*", SearchOption.AllDirectories).Any());
		Assert.True(_client.Calls.Count(c => c.StartsWith("file.read:", StringComparison.Ordinal)) < 40);   // stopped at the image limit, not endless
	}

	[Fact]
	public async Task OneByteChunksForever_AreCutOffByTheChunkCap()
	{
		_client.FileReadHandler = (_, offset, _) => Chunk("pic.png", "image/png", Constants.Limits.MaxPreviewImageBytes, offset, new byte[] { 1 }, eof: false);

		var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Loader().LoadAsync("h", saveToFile: false, null, CancellationToken.None));

		Assert.Contains("too many chunks", ex.Message);
		Assert.Equal(Constants.Limits.MaxPreviewChunks + 1, _client.Calls.Count(c => c.StartsWith("file.read:", StringComparison.Ordinal)));
		Assert.False(Directory.Exists(_temp.Paths.PreviewCacheDirectory) && Directory.EnumerateFiles(_temp.Paths.PreviewCacheDirectory, "*", SearchOption.AllDirectories).Any());
	}

	[Fact]
	public async Task AChunkWhoseLengthDoesNotMatchItsData_Fails()
	{
		_client.FileReadHandler = (_, offset, _) => Chunk("pic.png", "image/png", 10, offset, new byte[] { 1, 2, 3 }, eof: true, lengthOverride: 9);

		var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Loader().LoadAsync("h", saveToFile: false, null, CancellationToken.None));

		Assert.Contains("does not match", ex.Message);
	}
}
