namespace TessynDesktop.Client;

/// <summary>Tuning and identity of a <see cref="TessynClient"/>.</summary>
/// <remarks>Created by Claude</remarks>
public sealed record TessynClientOptions
{
	/// <summary>Sent in <c>client.hello</c>.</summary>
	public string ClientName { get; init; } = "TessynClient";

	/// <summary>Sent in <c>client.hello</c>.</summary>
	public string ClientVersion { get; init; } = "0.1.0";

	/// <summary>Per-call timeout of a JSON-RPC request.</summary>
	public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(30);

	/// <summary>Timeout of <c>attachment.import</c>: the daemon copies up to 200 MB in one synchronous call, so it gets far longer than <see cref="RequestTimeout"/>.</summary>
	public TimeSpan AttachmentImportTimeout { get; init; } = TimeSpan.FromMinutes(10);

	/// <summary>First reconnect delay; doubles per failed attempt up to <see cref="MaxBackoff"/>.</summary>
	public TimeSpan InitialBackoff { get; init; } = TimeSpan.FromMilliseconds(250);

	/// <summary>Cap of the reconnect delay.</summary>
	public TimeSpan MaxBackoff { get; init; } = TimeSpan.FromSeconds(10);
}
