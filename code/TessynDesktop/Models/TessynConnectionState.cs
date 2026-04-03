namespace TessynDesktop.Models;

/// <summary>
/// Connection state of the WebSocket link to the Tessyn daemon.
/// </summary>
/// <remarks>Created by Claude</remarks>
public enum TessynConnectionState
{
    /// <summary>Not connected to the daemon.</summary>
    Disconnected,

    /// <summary>Currently attempting to connect or reconnect.</summary>
    Connecting,

    /// <summary>WebSocket connected and protocol handshake complete.</summary>
    Connected,

    /// <summary>Connection lost, attempting automatic reconnection.</summary>
    Reconnecting
}
