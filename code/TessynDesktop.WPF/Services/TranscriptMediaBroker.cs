using Microsoft.Extensions.Logging;

namespace TessynDesktop.WPF.Services;

/// <summary>
/// The host side of the transcript's remote media (FR.26.4, decision A13, review V2/V7). It remembers exactly the media URLs the host
/// itself sent to the page (per entry), records which of them the user approved by a click, and is the only thing that turns a media request
/// of the page into a network request, through <see cref="RemoteMediaFetcher"/>. An image request is served only for an offered URL that is
/// allowlisted or approved; a video is always click-to-load and is downloaded to the preview cache (so it can seek), also for allowlisted
/// hosts. Nothing is requested before a click for a host that is not allowlisted. All state is cleared on every session load.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class TranscriptMediaBroker
{
	private static readonly IReadOnlyDictionary<string, string> VideoExtensions = new Dictionary<string, string>(StringComparer.Ordinal)
	{
		["video/mp4"] = ".mp4",
		["video/webm"] = ".webm",
		["video/ogg"] = ".ogv",
		["video/quicktime"] = ".mov",
		["video/x-m4v"] = ".m4v",
	};

	private readonly RemoteMediaFetcher _fetcher;
	private readonly PreviewFileStore _store;
	private readonly MediaProxyUrl _proxy;
	private readonly ILogger<TranscriptMediaBroker> _logger;
	private readonly Dictionary<int, HashSet<string>> _offered = new();
	private readonly HashSet<string> _approved = new(StringComparer.Ordinal);
	private readonly Dictionary<string, FetchedMedia> _images = new(StringComparer.Ordinal);
	private readonly Queue<string> _imageOrder = new();
	private readonly List<string> _videoFiles = [];
	private readonly object _gate = new();
	private readonly SemaphoreSlim _videoSlots = new(Constants.Limits.MaxConcurrentVideoDownloads);
	private CancellationTokenSource _loadCancellation = new();
	private long _imageBytes;
	private long _videoBytes;

	public TranscriptMediaBroker(RemoteMediaFetcher fetcher, PreviewFileStore store, MediaProxyUrl proxy, ILogger<TranscriptMediaBroker> logger)
	{
		_fetcher = fetcher;
		_store = store;
		_proxy = proxy;
		_logger = logger;
	}

	/// <summary>Remembers the media URLs sent to the page for an entry (replacing an earlier list of that entry).</summary>
	public void Offer(int entryIndex, IReadOnlyList<string>? urls)
	{
		lock (_gate)
		{
			if (urls is { Count: > 0 })
				_offered[entryIndex] = new HashSet<string>(urls, StringComparer.Ordinal);
			else
				_offered.Remove(entryIndex);
		}
	}

	/// <summary>
	/// Forgets everything (a new session is shown): running fetches and video downloads are cancelled (a partial file is deleted by the
	/// fetcher, a download that finishes in the race is deleted by the broker) and downloaded videos are deleted.
	/// </summary>
	public void Reset()
	{
		string[] videos;
		CancellationTokenSource previous;
		lock (_gate)
		{
			_offered.Clear();
			_approved.Clear();
			_images.Clear();
			_imageOrder.Clear();
			_imageBytes = 0;
			_videoBytes = 0;
			videos = [.. _videoFiles];
			_videoFiles.Clear();
			previous = _loadCancellation;
			_loadCancellation = new CancellationTokenSource();
		}

		previous.Cancel();
		foreach (var video in videos)
			_store.Delete(video);
	}

	/// <summary>Bytes held by the image cache (for tests and diagnostics).</summary>
	public long CachedImageBytes
	{
		get
		{
			lock (_gate)
				return _imageBytes;
		}
	}

	/// <summary>Test seam: sets the bytes counted as downloaded in this view.</summary>
	public void ForceDownloadedBytesForTest(long bytes)
	{
		lock (_gate)
			_videoBytes = bytes;
	}

	private CancellationToken CurrentLoadToken()
	{
		lock (_gate)
			return _loadCancellation.Token;
	}

	/// <summary>The user clicked "Load preview" for <paramref name="url"/> of entry <paramref name="entryIndex"/>: approved only when the host offered exactly that URL there.</summary>
	public bool TryApprove(int entryIndex, string url)
	{
		lock (_gate)
		{
			if (!_offered.TryGetValue(entryIndex, out var urls) || !urls.Contains(url))
				return false;

			_approved.Add(url);
			return true;
		}
	}

	/// <summary>Serves an image request of the page: the URL must be offered, and allowlisted or approved; otherwise <see cref="MediaRefusedException"/>.</summary>
	public async Task<FetchedMedia> ServeImageAsync(Uri request, CancellationToken cancellationToken)
	{
		if (!_proxy.TryParse(request, out var remote))
			throw new MediaRefusedException("Not a media request.");

		bool approved;
		lock (_gate)
		{
			if (!_offered.Values.Any(urls => urls.Contains(remote)))
				throw new MediaRefusedException("The address was not offered by the host.");

			if (_images.TryGetValue(remote, out var cached))
				return cached;

			approved = _approved.Contains(remote);
		}

		if (!Uri.TryCreate(remote, UriKind.Absolute, out var uri))
			throw new MediaRefusedException("Invalid address.");

		using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, CurrentLoadToken());
		var image = await _fetcher.FetchImageAsync(uri, approved, linked.Token);
		lock (_gate)
		{
			// The cache is bounded by count and by total bytes; an image larger than the byte cap is served but not kept.
			if (!linked.IsCancellationRequested && image.Bytes.Length <= Constants.Limits.MaxImageCacheBytes && _images.TryAdd(remote, image))
			{
				_imageOrder.Enqueue(remote);
				_imageBytes += image.Bytes.Length;
				while (_imageOrder.Count > Constants.Limits.MaxCachedRemoteImages || _imageBytes > Constants.Limits.MaxImageCacheBytes)
				{
					var oldest = _imageOrder.Dequeue();
					if (_images.Remove(oldest, out var removed))
						_imageBytes -= removed.Bytes.Length;
				}
			}
		}

		return image;
	}

	/// <summary>Downloads an approved video to the preview cache and returns the URL the page plays it from.</summary>
	public async Task<string> DownloadVideoAsync(string url, CancellationToken cancellationToken)
	{
		bool approved;
		lock (_gate)
			approved = _approved.Contains(url);

		if (!approved || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
			throw new MediaRefusedException("The video was not approved by a click.");

		long remaining;
		lock (_gate)
			remaining = Constants.Limits.MaxVideoBytesPerLoad - _videoBytes;
		if (remaining <= 0)
			throw new MediaRefusedException("The video download limit for this view is used up.");

		using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, CurrentLoadToken());
		await _videoSlots.WaitAsync(linked.Token);   // at most two downloads at a time; a Reset frees the waiting ones
		string path;
		try
		{
			path = await _fetcher.DownloadVideoAsync(uri, approvedByClick: true, type =>
			{
				if (!VideoExtensions.TryGetValue(type, out var extension))
					throw new MediaRefusedException($"The video type '{type}' is not supported.");

				return _store.NewFilePath(extension);
			}, linked.Token, remaining);
		}
		finally
		{
			_videoSlots.Release();
		}

		if (linked.IsCancellationRequested)
		{
			_store.Delete(path);   // finished in the race with a Reset: nothing may be left behind
			throw new OperationCanceledException(linked.Token);
		}

		lock (_gate)
		{
			_videoFiles.Add(path);
			_videoBytes += new FileInfo(path).Length;
		}

		_logger.LogInformation("Video from {Host} downloaded to the preview cache", uri.Host);
		return _store.UrlOf(path);
	}
}
