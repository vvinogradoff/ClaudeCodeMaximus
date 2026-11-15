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

	/// <summary>Uploads that may run at the same time; the next one is answered 429. Each occupies one daemon dispatch worker while the daemon downloads.</summary>
	public int MaxConcurrentUploads { get; set; } = Constants.Limits.MaxConcurrentUploads;

	/// <summary>Most bytes the blob folder may hold (blobs waiting for the daemon); an upload that would exceed it is answered 507.</summary>
	public long MaxBlobDirectoryBytes { get; set; } = Constants.Limits.MaxBlobDirectoryBytes;

	/// <summary>A blob nobody fetched is deleted after this long (checked at start-up and on each upload).</summary>
	public double BlobTtlMinutes { get; set; } = Constants.Defaults.BlobTtlMinutes;
}
