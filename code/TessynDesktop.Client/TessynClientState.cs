namespace TessynDesktop.Client;

/// <summary>Connection state of an <see cref="ITessynClient"/>.</summary>
/// <remarks>Created by Claude</remarks>
public enum TessynClientState
{
	/// <summary>Not connected: before <see cref="ITessynClient.ConnectAsync"/>, after a failed connect, or after disposal.</summary>
	Disconnected,

	/// <summary>The first handshake (<c>client.hello</c>) is in progress.</summary>
	Connecting,

	/// <summary>Handshake done and the notification stream is up.</summary>
	Connected,

	/// <summary>The stream dropped; the client is retrying with backoff. Method calls still work while the daemon answers.</summary>
	Reconnecting,

	/// <summary>The daemon's <c>minProtocolVersion</c> is newer than this client speaks. Terminal.</summary>
	Incompatible,

	/// <summary>The daemon answered 401 or 403: the token is wrong or stale. Terminal until a new client is created.</summary>
	AuthFailed,
}
