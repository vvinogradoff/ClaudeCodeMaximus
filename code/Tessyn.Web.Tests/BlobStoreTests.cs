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
	public async Task N1_A_claimed_blob_whose_delete_keeps_failing_is_released_and_the_sweep_removes_it_once_it_can()
	{
		var store = NewStore(o => o.BlobTtlMinutes = 60);
		var blob = await SaveAsync(store, [1, 2, 3]);
		Assert.True(store.TryClaim(blob.Id, out _));
		File.SetAttributes(blob.Path, FileAttributes.ReadOnly);   // File.Delete refuses a read-only file on Windows
		if (!OperatingSystem.IsWindows())
		{
			File.SetAttributes(blob.Path, FileAttributes.Normal);
			return;   // Unix deletes by the folder's permissions; the failure cannot be produced the same way.
		}

		store.Delete(blob.Id);
		store.Delete(blob.Id);
		store.Delete(blob.Id);   // the third failure releases the claim

		Assert.True(File.Exists(blob.Path));
		Assert.False(store.TryClaim(blob.Id, out _));   // released, but never claimable again
		store.Sweep();
		Assert.True(File.Exists(blob.Path));   // the sweep retries, and the file still resists

		File.SetAttributes(blob.Path, FileAttributes.Normal);
		store.Sweep();   // young as it is, an abandoned blob goes at the next sweep
		Assert.False(File.Exists(blob.Path));
		Assert.Equal(0, store.UsedBytes());
	}

	[Fact]
	public async Task N1_A_claim_with_a_failed_delete_is_released_after_twice_the_ttl_and_one_without_stays()
	{
		if (!OperatingSystem.IsWindows())
			return;   // The failed delete is made with a read-only file, which only Windows refuses to delete.

		var store = NewStore(o => o.BlobTtlMinutes = 60);
		var stuck = await SaveAsync(store, [1, 2, 3]);
		var downloading = await SaveAsync(store, [4, 5, 6]);
		Assert.True(store.TryClaim(stuck.Id, out _));
		Assert.True(store.TryClaim(downloading.Id, out _));
		File.SetAttributes(stuck.Path, FileAttributes.ReadOnly);
		store.Delete(stuck.Id);   // one failed delete: not yet three
		Age(stuck, TimeSpan.FromMinutes(100));
		Age(downloading, TimeSpan.FromHours(5));

		store.Sweep();
		Assert.True(File.Exists(stuck.Path));   // past one TTL only
		Assert.True(File.Exists(downloading.Path));   // no failed delete: a download, however long it takes

		Age(stuck, TimeSpan.FromMinutes(121));
		store.Sweep();   // released, retried, still read-only
		Assert.False(store.TryClaim(stuck.Id, out _));
		File.SetAttributes(stuck.Path, FileAttributes.Normal);
		store.Sweep();
		Assert.False(File.Exists(stuck.Path));
		Assert.True(File.Exists(downloading.Path));
	}

	[Fact]
	public void N3_On_Unix_a_blob_folder_that_is_not_private_to_the_user_is_never_chmoded_and_the_store_still_starts()
	{
		if (OperatingSystem.IsWindows())
			return;   // Windows keeps its ACLs; the permission bits do not exist.

		Directory.CreateDirectory(_dir);
		var open = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;
		File.SetUnixFileMode(_dir, open);

		var store = NewStore();   // must not throw

		Assert.Equal(open, File.GetUnixFileMode(_dir));   // the existing folder is left alone
		var blob = SaveAsync(store, [1]).GetAwaiter().GetResult();
		Assert.False(blob.Path.StartsWith(_dir, StringComparison.Ordinal));   // the blob waits in the store's own private folder
		Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(System.IO.Path.GetDirectoryName(blob.Path)!));
		Directory.Delete(System.IO.Path.GetDirectoryName(blob.Path)!, true);
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
