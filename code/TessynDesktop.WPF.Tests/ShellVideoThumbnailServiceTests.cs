using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.Tests.Fakes;

namespace TessynDesktop.WPF.Tests;

/// <summary>
/// The first-frame picture of a video (FR.26.6) from the real Windows Shell thumbnail API, with a 3 KB H.264 clip (<c>TestData/tiny.mp4</c>,
/// 160x90, one second): a real frame comes out as a PNG; a file that is not a video, a missing file and an unknown type give none, without throwing.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class ShellVideoThumbnailServiceTests : IDisposable
{
	private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

	private readonly string _dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "tessyn-thumb-" + Guid.NewGuid().ToString("N"))).FullName;
	private readonly ShellVideoThumbnailService _service = new(NullLogger<ShellVideoThumbnailService>.Instance);

	public void Dispose() => TestDirectory.Delete(_dir);

	public static string TinyVideo([CallerFilePath] string here = "") => Path.Combine(Path.GetDirectoryName(here)!, "TestData", "tiny.mp4");

	[Fact]
	public async Task ARealVideo_GivesAPngOfItsFirstFrame()
	{
		var video = Path.Combine(_dir, "clip.mp4");
		File.Copy(TinyVideo(), video);
		var png = Path.Combine(_dir, "frame.png");

		Assert.True(await _service.TryCreateAsync(video, png, 360, CancellationToken.None));

		var bytes = await File.ReadAllBytesAsync(png);
		Assert.Equal(PngSignature, bytes[..8]);
		Assert.True(bytes.Length > 200);
		var width = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16, 4));
		var height = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20, 4));
		Assert.InRange(width, 80, 720);
		Assert.InRange(height, 45, 720);
		Assert.True(width > height, "a 16:9 clip must stay landscape");
	}

	[Fact]
	public async Task AFileThatIsNotAVideo_GivesNoFrame()
	{
		var fake = Path.Combine(_dir, "fake.mp4");
		await File.WriteAllTextAsync(fake, "this is not a video at all");
		var png = Path.Combine(_dir, "frame.png");

		Assert.False(await _service.TryCreateAsync(fake, png, 360, CancellationToken.None));
		Assert.False(File.Exists(png));
	}

	[Fact]
	public async Task AMissingFile_GivesNoFrame_AndDoesNotThrow()
	{
		Assert.False(await _service.TryCreateAsync(Path.Combine(_dir, "gone.mp4"), Path.Combine(_dir, "frame.png"), 360, CancellationToken.None));
	}

	[Fact]
	public async Task ACancelledCall_Cancels()
	{
		var video = Path.Combine(_dir, "clip.mp4");
		File.Copy(TinyVideo(), video);
		using var cts = new CancellationTokenSource();
		cts.Cancel();

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _service.TryCreateAsync(video, Path.Combine(_dir, "f.png"), 360, cts.Token));
	}
}
