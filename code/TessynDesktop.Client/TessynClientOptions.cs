namespace TessynDesktop.Client;

/// <summary>Tuning and identity of a <see cref="TessynClient"/>.</summary>
/// <remarks>Created by Claude</remarks>
public sealed record TessynClientOptions
{
	/// <summary>Sent in <c>client.hello</c>.</summary>
	public string ClientName { get; init; } = "TessynClient";

	/// <summary>Sent in <c>client.hello</c>.</summary>
	public string ClientVersion { get; init; } = "0.1.0";

	/// <summary>
	/// Names this client to the daemon (package J2): sent with <c>client.hello</c> and on the notification stream's request, so the daemon can tie
	/// <see cref="Presents"/> to the life of the stream. Null (the default) means an anonymous client.
	/// </summary>
	public string? ClientId { get; init; }

	/// <summary>
	/// Notification channels this client shows itself (<c>"toast"</c>, <c>"sound"</c>). Needs <see cref="ClientId"/>. Registered with every hello, so a
	/// reconnect or a daemon restart registers again. While the stream is open the desktop host does not show those notifications.
	/// </summary>
	public IReadOnlyList<string> Presents { get; init; } = [];

	/// <summary>Per-call timeout of a JSON-RPC request.</summary>
	public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(30);

	/// <summary>Timeout of <c>attachment.import</c>: the daemon copies up to 200 MB in one synchronous call, so it gets far longer than <see cref="RequestTimeout"/>.</summary>
	public TimeSpan AttachmentImportTimeout { get; init; } = TimeSpan.FromMinutes(10);

	/// <summary>
	/// Timeout of <c>mcp.authorize</c>: the daemon waits up to two minutes for the user in the browser, and <c>models.refresh</c> for up to 30 s, both longer
	/// than <see cref="RequestTimeout"/>.
	/// </summary>
	public TimeSpan InteractiveRequestTimeout { get; init; } = TimeSpan.FromMinutes(3);

	/// <summary>First reconnect delay; doubles per failed attempt up to <see cref="MaxBackoff"/>.</summary>
	public TimeSpan InitialBackoff { get; init; } = TimeSpan.FromMilliseconds(250);

	/// <summary>Cap of the reconnect delay.</summary>
	public TimeSpan MaxBackoff { get; init; } = TimeSpan.FromSeconds(10);
}
