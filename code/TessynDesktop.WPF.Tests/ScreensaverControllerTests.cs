using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.State;
using TessynDesktop.WPF.Tests.Fakes;

namespace TessynDesktop.WPF.Tests;

/// <summary>The screensaver's rules: idle timer, slideshow, ending on input (W10a, FR.4 settings).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class ScreensaverControllerTests : IDisposable
{
	private sealed class FakeImages : IScreensaverImages
	{
		public List<string> Files { get; } = [];

		public int Listed { get; private set; }

		public IReadOnlyList<string> List(string directory)
		{
			Listed++;
			return Files.ToList();
		}
	}

	private static readonly DateTime T0 = new(2026, 11, 16, 9, 0, 0, DateTimeKind.Utc);

	private readonly ClientSettingsStore _store = new(new MemoryClientState(), NullLogger<ClientSettingsStore>.Instance);
	private readonly FakeImages _images = new() { Files = { "a.png", "b.png", "c.png" } };
	private readonly ScreensaverController _controller;
	private int _changes;

	public ScreensaverControllerTests()
	{
		_store.Update<ScreensaverSettings>(s =>
		{
			s.Directory = @"D:\pics";
			s.TimeoutSeconds = 60;
			s.SlideshowIntervalSeconds = 10;
		});
		_controller = new ScreensaverController(_store, _images, T0);
		_controller.Changed += (_, _) => _changes++;
	}

	public void Dispose()
	{
	}

	[Fact]
	public void StartsOnlyAfterTheIdleTimeout()
	{
		_controller.Tick(T0.AddSeconds(59));
		Assert.False(_controller.IsActive);

		_controller.Tick(T0.AddSeconds(60));

		Assert.True(_controller.IsActive);
		Assert.Equal("a.png", _controller.CurrentImage);
		Assert.Equal(1, _changes);
	}

	[Fact]
	public void Input_RestartsTheIdleTimer()
	{
		_controller.NoteInput(T0.AddSeconds(50));

		_controller.Tick(T0.AddSeconds(100));   // 50 s since the input
		Assert.False(_controller.IsActive);

		_controller.Tick(T0.AddSeconds(110));
		Assert.True(_controller.IsActive);
	}

	[Fact]
	public void Slideshow_MovesOnEveryInterval_AndWrapsAround()
	{
		_controller.Tick(T0.AddSeconds(60));

		_controller.Tick(T0.AddSeconds(69));
		Assert.Equal("a.png", _controller.CurrentImage);

		_controller.Tick(T0.AddSeconds(70));
		Assert.Equal("b.png", _controller.CurrentImage);
		_controller.Tick(T0.AddSeconds(80));
		Assert.Equal("c.png", _controller.CurrentImage);
		_controller.Tick(T0.AddSeconds(90));
		Assert.Equal("a.png", _controller.CurrentImage);
	}

	[Fact]
	public void AnyInput_EndsIt_AndReportsThatItDid()
	{
		_controller.Tick(T0.AddSeconds(60));

		Assert.True(_controller.NoteInput(T0.AddSeconds(61)));

		Assert.False(_controller.IsActive);
		Assert.Null(_controller.CurrentImage);
		Assert.False(_controller.NoteInput(T0.AddSeconds(62)));   // an ordinary input afterwards is not consumed
		Assert.Equal(2, _changes);
	}

	[Fact]
	public void TimeoutZero_TurnsItOff_AlsoWhileItRuns()
	{
		_controller.Tick(T0.AddSeconds(60));
		Assert.True(_controller.IsActive);

		_store.Update<ScreensaverSettings>(s => s.TimeoutSeconds = 0);
		_controller.Tick(T0.AddSeconds(61));

		Assert.False(_controller.IsActive);
		_controller.Tick(T0.AddHours(5));
		Assert.False(_controller.IsActive);
	}

	[Fact]
	public void ChangedSettings_ApplyOnTheNextTick()
	{
		_store.Update<ScreensaverSettings>(s => s.TimeoutSeconds = 5);

		_controller.Tick(T0.AddSeconds(5));

		Assert.True(_controller.IsActive);
	}

	[Fact]
	public void EmptyFolder_StillCoversTheWindow_WithNoImage()
	{
		_images.Files.Clear();

		_controller.Tick(T0.AddSeconds(60));

		Assert.True(_controller.IsActive);
		Assert.Null(_controller.CurrentImage);
		_controller.Tick(T0.AddSeconds(200));   // no slideshow to run
		Assert.Equal(1, _changes);
	}

	[Fact]
	public void SingleImage_StaysOnScreen()
	{
		_images.Files.RemoveRange(1, 2);
		_controller.Tick(T0.AddSeconds(60));

		_controller.Tick(T0.AddSeconds(120));

		Assert.Equal("a.png", _controller.CurrentImage);
		Assert.Equal(1, _changes);
	}

	[Fact]
	public void TheFolderIsReadWhenItStarts_NotOnEveryTick()
	{
		_controller.Tick(T0.AddSeconds(10));
		_controller.Tick(T0.AddSeconds(60));
		_controller.Tick(T0.AddSeconds(70));

		Assert.Equal(1, _images.Listed);
	}

	[Fact]
	public void FolderListing_KeepsOnlyImagesInNameOrder_AndToleratesAMissingFolder()
	{
		var dir = Path.Combine(Path.GetTempPath(), "tessyn-ss-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(dir);
		try
		{
			foreach (var name in new[] { "b.JPG", "a.png", "notes.txt", "c.webp", "d.exe" })
				File.WriteAllText(Path.Combine(dir, name), "x");

			var listed = new FolderScreensaverImages().List(dir).Select(Path.GetFileName).ToList();

			Assert.Equal(["a.png", "b.JPG", "c.webp"], listed);
			Assert.Empty(new FolderScreensaverImages().List(Path.Combine(dir, "missing")));
			Assert.Empty(new FolderScreensaverImages().List(string.Empty));
		}
		finally
		{
			TestDirectory.Delete(dir);
		}
	}
}
