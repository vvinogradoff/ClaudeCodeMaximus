namespace Tessyn.Web.Options;

/// <summary>Browser uploads that the daemon pulls over the link (decision A12).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class AttachmentOptions
{
	/// <summary>Largest file a browser may upload.</summary>
	public long MaxUploadBytes { get; set; } = Constants.Limits.MaxUploadBytes;

	/// <summary>Where blobs wait for the daemon. Empty: a folder under the system temp directory. Blobs are one-time and short-lived.</summary>
	public string BlobDirectory { get; set; } = string.Empty;

	/// <summary>How long the server waits for the daemon to fetch and store a blob.</summary>
	public double FetchTimeoutSeconds { get; set; } = Constants.Defaults.AttachmentFetchTimeoutSeconds;

	/// <summary>A blob nobody fetched is deleted after this long (checked at start-up and on each upload).</summary>
	public double BlobTtlMinutes { get; set; } = Constants.Defaults.BlobTtlMinutes;
}
