using System;
using System.IO;
using System.Threading.Tasks;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <remarks>Created by Claude</remarks>
public sealed class RemoteTrackersTests : IDisposable
{
	private readonly string _tempDir;
	private readonly SessionFileService _files;

	public RemoteTrackersTests()
	{
		_tempDir = Path.Combine(Path.GetTempPath(), "cm_test_trackers_" + Path.GetRandomFileName());
		Directory.CreateDirectory(_tempDir);
		var settings = new AppSettingsService(_tempDir);
		settings.Settings.SessionFilesRoot = _tempDir;
		_files = new SessionFileService(settings);
	}

	public void Dispose() => Directory.Delete(_tempDir, recursive: true);

	// ── Tree version ─────────────────────────────────────────────────────────────

	[Fact]
	public void TreeVersion_StartsAtZero_AndIncrementsByOne()
	{
		var tracker = new TreeVersionTracker();

		Assert.Equal(0, tracker.Current);
		Assert.Equal(1, tracker.Increment());
		Assert.Equal(2, tracker.Increment());
		Assert.Equal(2, tracker.Current);
	}

	[Fact]
	public void TreeVersion_IsThreadSafe()
	{
		var tracker = new TreeVersionTracker();

		Parallel.For(0, 5000, _ => tracker.Increment());

		Assert.Equal(5000, tracker.Current);
	}

	// ── Epochs ───────────────────────────────────────────────────────────────────

	[Fact]
	public void Epoch_IsZeroUntilTheFileIsRewritten()
	{
		using var tracker = new SessionEpochTracker(_files);

		Assert.Equal(0, tracker.GetEpoch("a.txt"));
	}

	[Fact]
	public void Epoch_IncrementsPerRewrite_PerFile()
	{
		using var tracker = new SessionEpochTracker(_files);
		var a = _files.CreateSessionFile();
		var b = _files.CreateSessionFile();

		_files.RewriteSessionFile(a, "x");
		_files.RewriteSessionFile(a, "y");
		_files.WriteSessionFile(b, []);

		Assert.Equal(2, tracker.GetEpoch(a));
		Assert.Equal(1, tracker.GetEpoch(b));
	}

	[Fact]
	public void Epoch_IsNotBumpedByAppends()
	{
		using var tracker = new SessionEpochTracker(_files);
		var a = _files.CreateSessionFile();

		_files.AppendMessage(a, "USER", "hello");

		Assert.Equal(0, tracker.GetEpoch(a));
	}

	[Fact]
	public void Epoch_StopsCountingAfterDispose()
	{
		var tracker = new SessionEpochTracker(_files);
		var a = _files.CreateSessionFile();
		tracker.Dispose();

		_files.RewriteSessionFile(a, "x");

		Assert.Equal(0, tracker.GetEpoch(a));
	}
}
