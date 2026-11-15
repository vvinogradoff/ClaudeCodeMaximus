using System.Net;

namespace TessynDesktop.WPF.Services;

/// <summary>
/// The only code that fetches remote media for the transcript (FR.26.4, review V2): the page never touches the network. A request is
/// allowed when the URL is https on the default port without credentials, the host is public (no loopback or private addresses), and
/// the host is on the <see cref="MediaPreviewPolicy"/> allowlist or the user clicked that URL. Redirects are never followed by the HTTP
/// stack; each hop is re-checked against the same policy (https, public, allowlisted or the host of the clicked URL), at most
/// <see cref="Constants.Limits.MaxMediaRedirects"/>. The content type must be image/* (not svg) or video/*, the size is capped
/// by the declared length and by the bytes actually read, and the request has a timeout. The <see cref="HttpClient"/> must not follow
/// redirects or keep cookies (see <see cref="RemoteMediaHttpClientFactory"/>); no Referer, cookie or credential is sent.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class RemoteMediaFetcher
{
	private readonly HttpClient _http;
	private readonly MediaPreviewPolicy _policy;
	private readonly PrivateAddressCheck _addresses;

	public RemoteMediaFetcher(HttpClient http, MediaPreviewPolicy policy, PrivateAddressCheck addresses)
	{
		_http = http;
		_policy = policy;
		_addresses = addresses;
	}

	/// <summary>True when a request for <paramref name="url"/> may start at all (the initial check of <see cref="FetchImageAsync"/>).</summary>
	public bool MayRequest(Uri url, bool approvedByClick) => IsSafeUrl(url) && IsPermitted(url, approvedByClick);

	/// <summary>The approval rule alone, without the address check: a click on that URL, or an allowlisted host.</summary>
	public bool IsPermitted(Uri url, bool approvedByClick) => approvedByClick || _policy.IsAllowedHost(url.Host);

	/// <summary>Fetches an image; refuses with <see cref="MediaRefusedException"/>.</summary>
	public async Task<FetchedMedia> FetchImageAsync(Uri url, bool approvedByClick, CancellationToken cancellationToken)
	{
		using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeout.CancelAfter(Constants.Timeouts.RemoteImage);
		using var response = await SendAsync(url, approvedByClick, timeout.Token);
		var type = ContentTypeOf(response, isVideo: false);
		var bytes = await ReadAsync(response, Constants.Limits.MaxRemoteImageBytes, destination: null, timeout.Token);
		return new FetchedMedia(bytes!, type);
	}

	/// <summary>
	/// Downloads a video. <paramref name="destinationFor"/> maps the checked content type to the file to write (it may refuse a type by
	/// throwing); the file is deleted again on any failure. Returns the path.
	/// </summary>
	public async Task<string> DownloadVideoAsync(Uri url, bool approvedByClick, Func<string, string> destinationFor, CancellationToken cancellationToken, long maxBytes = Constants.Limits.MaxRemoteVideoBytes)
	{
		using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeout.CancelAfter(Constants.Timeouts.RemoteVideo);
		using var response = await SendAsync(url, approvedByClick, timeout.Token);
		var destinationPath = destinationFor(ContentTypeOf(response, isVideo: true));
		try
		{
			await using var file = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
			await ReadAsync(response, Math.Min(maxBytes, Constants.Limits.MaxRemoteVideoBytes), file, timeout.Token);
			return destinationPath;
		}
		catch
		{
			try
			{
				File.Delete(destinationPath);
			}
			catch (IOException)
			{
			}

			throw;
		}
	}

	private async Task<HttpResponseMessage> SendAsync(Uri origin, bool approvedByClick, CancellationToken cancellationToken)
	{
		if (!MayRequest(origin, approvedByClick))
			throw new MediaRefusedException("The address is not allowed.");

		var current = origin;
		for (var hop = 0; ; hop++)
		{
			using var request = new HttpRequestMessage(HttpMethod.Get, current);
			var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
			if (IsRedirect(response.StatusCode))
			{
				var location = response.Headers.Location;
				response.Dispose();
				if (location == null || hop >= Constants.Limits.MaxMediaRedirects)
					throw new MediaRefusedException("Too many redirects or no target.");

				var next = location.IsAbsoluteUri ? location : new Uri(current, location);
				if (!IsSafeUrl(next) || !(_policy.IsAllowedHost(next.Host) || string.Equals(next.Host, origin.Host, StringComparison.OrdinalIgnoreCase)))
					throw new MediaRefusedException("A redirect leads to a host that is not allowed.");

				current = next;
				continue;
			}

			if (!response.IsSuccessStatusCode)
			{
				var code = (int)response.StatusCode;
				response.Dispose();
				throw new MediaRefusedException($"The server answered {code}.");
			}

			return response;
		}
	}

	private static bool IsRedirect(HttpStatusCode code) => code is HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther
		or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect or HttpStatusCode.MultipleChoices;

	private bool IsSafeUrl(Uri url) =>
		url.Scheme == Uri.UriSchemeHttps && url.IsDefaultPort && url.UserInfo.Length == 0 && _addresses.IsPublicHost(url.Host);

	private static string ContentTypeOf(HttpResponseMessage response, bool isVideo)
	{
		var type = response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant() ?? string.Empty;
		var ok = isVideo ? type.StartsWith("video/", StringComparison.Ordinal)
			: type.StartsWith("image/", StringComparison.Ordinal) && !type.StartsWith("image/svg", StringComparison.Ordinal);
		if (!ok)
			throw new MediaRefusedException($"The content type '{type}' is not allowed.");

		return type;
	}

	private static async Task<byte[]?> ReadAsync(HttpResponseMessage response, long cap, Stream? destination, CancellationToken cancellationToken)
	{
		if (response.Content.Headers.ContentLength is > 0 and var declared && declared > cap)
			throw new MediaRefusedException("The file is too large.");

		await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
		using var memory = destination == null ? new MemoryStream() : null;
		var sink = destination ?? memory!;
		var buffer = new byte[81920];
		long total = 0;
		int read;
		while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
		{
			total += read;
			if (total > cap)
				throw new MediaRefusedException("The file is too large.");

			await sink.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
		}

		return memory?.ToArray();
	}
}
