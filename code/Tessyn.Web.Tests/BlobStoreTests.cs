using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Tessyn.Web.Link;
using Tessyn.Web.Options;
using Tessyn.Web.Tests.Support;

namespace Tessyn.Web.Tests;

/// <summary>The temporary blob store behind the attachment upload: one-time claims, deletion order, sweeping, quota, permissions.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class BlobStoreTests : IDisposable
{
	private readonly string _dir = Path.Combine(Path.GetTempPath(), "tessyn-blobstore-tests-" + Guid.NewGuid().ToString("N"));
	private readonly ManualTimeProvider _clock = new();

	private BlobStore NewStore(Action<AttachmentOptions>? configure = null)
	{
		var options = new TessynWebOptions { Attachments = new AttachmentOptions { BlobDirectory = _dir } };
		configure?.Invoke(options.Attachments);
		return new BlobStore(Microsoft.Extensions.Options.Options.Create(options), _clock, NullLogger<BlobStore>.Instance);
	}

	private static async Task<StoredBlob> SaveAsync(BlobStore store, byte[] bytes, long max = 1_000_000) =>
		await store.SaveAsync(new MemoryStream(bytes), max, CancellationToken.None);

	private void Age(StoredBlob blob, TimeSpan age) => File.SetLastWriteTimeUtc(blob.Path, _clock.GetUtcNow().UtcDateTime - age);

	public void Dispose()
	{
		if (Directory.Exists(_dir))
			Directory.Delete(_dir, true);
	}

	[Fact]
	public async Task Save_streams_hashes_and_counts_the_bytes()
	{
		var store = NewStore();
		var bytes = new byte[10_000];
		new Random(1).NextBytes(bytes);

		var blob = await SaveAsync(store, bytes);

		Assert.Equal(10_000, blob.Size);
		Assert.Equal(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)), blob.Sha256);
		Assert.Equal(bytes, await File.ReadAllBytesAsync(blob.Path));
		Assert.Equal(10_000, store.UsedBytes());
	}

	[Fact]
	public async Task Two_concurrent_claims_of_one_blob_have_exactly_one_winner()
	{
		var store = NewStore();
		var blob = await SaveAsync(store, [1, 2, 3]);

		var results = await Task.WhenAll(Enumerable.Range(0, 32).Select(i => Task.Run(() => store.TryClaim(blob.Id, out var winner))));

		Assert.Equal(1, results.Count(r => r));
	}

	[Fact]
	public async Task Delete_while_the_download_still_holds_the_file_keeps_the_claim_so_nobody_can_claim_it_again()
	{
		if (!OperatingSystem.IsWindows())
			return;   // Unix unlinks an open file, so the case cannot arise there.

		var store = NewStore();
		var blob = await SaveAsync(store, [1, 2, 3]);
		Assert.True(store.TryClaim(blob.Id, out var claimed));

		using (new FileStream(claimed.Path, FileMode.Open, FileAccess.Read, FileShare.Read))
		{
			store.Delete(blob.Id);

			Assert.True(File.Exists(blob.Path));
			Assert.False(store.TryClaim(blob.Id, out _));
		}

		store.Delete(blob.Id);
		Assert.False(File.Exists(blob.Path));
		Assert.False(store.TryClaim(blob.Id, out _));
	}

	[Fact]
	public async Task A_deleted_blob_cannot_be_claimed_and_delete_is_idempotent()
	{
		var store = NewStore();
		var blob = await SaveAsync(store, [1]);

		store.Delete(blob.Id);
		store.Delete(blob.Id);
		store.Delete("not-an-id");

		Assert.False(store.TryClaim(blob.Id, out _));
		Assert.False(File.Exists(blob.Path));
	}

	[Fact]
	public async Task Sweep_removes_an_unclaimed_blob_after_the_ttl_by_the_clock_and_keeps_a_young_one()
	{
		var store = NewStore(o => o.BlobTtlMinutes = 60);
		var old = await SaveAsync(store, [1]);
		var young = await SaveAsync(store, [2]);
		Age(old, TimeSpan.FromMinutes(61));
		Age(young, TimeSpan.FromMinutes(59));

		store.Sweep();

		Assert.False(File.Exists(old.Path));
		Assert.True(File.Exists(young.Path));
	}

	[Fact]
	public async Task Sweep_does_not_delete_a_blob_that_is_claimed_and_being_downloaded()
	{
		var store = NewStore(o => o.BlobTtlMinutes = 60);
		var blob = await SaveAsync(store, [1, 2, 3]);
		Assert.True(store.TryClaim(blob.Id, out _));
		Age(blob, TimeSpan.FromHours(5));

		store.Sweep();

		Assert.True(File.Exists(blob.Path));
	}

	[Fact]
	public async Task A_new_store_sweeps_what_an_earlier_run_left_behind()
	{
		var first = NewStore(o => o.BlobTtlMinutes = 60);
		var left = await SaveAsync(first, [1]);
		Age(left, TimeSpan.FromHours(3));

		NewStore(o => o.BlobTtlMinutes = 60);

		Assert.False(File.Exists(left.Path));
	}

	[Fact]
	public async Task A_stream_over_the_limit_throws_and_leaves_no_file()
	{
		var store = NewStore();

		await Assert.ThrowsAsync<BlobTooLargeException>(() => SaveAsync(store, new byte[5000], max: 4096));

		Assert.Empty(Directory.GetFiles(_dir));
	}

	[Fact]
	public async Task The_byte_quota_stops_an_upload_that_would_exceed_it_and_frees_the_space_on_delete()
	{
		var store = NewStore(o => o.MaxBlobDirectoryBytes = 10_000);
		var first = await SaveAsync(store, new byte[6000]);

		await Assert.ThrowsAsync<BlobStorageFullException>(() => SaveAsync(store, new byte[6000]));
		Assert.Single(Directory.GetFiles(_dir));

		store.Delete(first.Id);
		var again = await SaveAsync(store, new byte[6000]);
		Assert.Equal(6000, again.Size);
	}

	[Fact]
	public async Task A_full_quota_refuses_before_reading_the_body()
	{
		var store = NewStore(o => o.MaxBlobDirectoryBytes = 1000);
		await SaveAsync(store, new byte[1000]);

		await Assert.ThrowsAsync<BlobStorageFullException>(() => store.SaveAsync(new ThrowingStream(), 1_000_000, CancellationToken.None));
	}

	private sealed class ThrowingStream : Stream
	{
		public override int Read(byte[] buffer, int offset, int count) => throw new InvalidOperationException("must not be read");
		public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => throw new InvalidOperationException("must not be read");
		public override bool CanRead => true;
		public override bool CanSeek => false;
		public override bool CanWrite => false;
		public override long Length => throw new NotSupportedException();
		public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
		public override void Flush() { }
		public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
		public override void SetLength(long value) => throw new NotSupportedException();
		public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
	}

	[Theory]
	[InlineData(unchecked((int)0x80070070), true)]
	[InlineData(unchecked((int)0x80070027), true)]
	[InlineData(28, true)]
	[InlineData(unchecked((int)0x80070020), false)]
	[InlineData(5, false)]
	public void The_disk_full_errors_of_Windows_and_Unix_are_recognised(int hresult, bool expected)
	{
		Assert.Equal(expected, BlobStore.IsDiskFull(new IOException("x", hresult)));
	}

	[Fact]
	public async Task On_Unix_the_folder_is_private_to_the_user_and_so_are_the_files()
	{
		if (OperatingSystem.IsWindows())
			return;   // Windows keeps its ACLs; the permission bits do not exist.

		var store = NewStore();
		var blob = await SaveAsync(store, [1]);

		Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(_dir));
		Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(blob.Path));
	}

	[Fact]
	public void The_upload_limiter_gives_out_the_configured_number_of_slots()
	{
		var limiter = new UploadLimiter(Microsoft.Extensions.Options.Options.Create(new TessynWebOptions { Attachments = new AttachmentOptions { MaxConcurrentUploads = 2 } }));

		Assert.True(limiter.TryEnter());
		Assert.True(limiter.TryEnter());
		Assert.False(limiter.TryEnter());
		limiter.Exit();
		Assert.True(limiter.TryEnter());
	}
}
