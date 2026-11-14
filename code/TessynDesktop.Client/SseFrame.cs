namespace TessynDesktop.Client;

/// <summary>One server-sent event: its <c>id:</c> (0 when absent) and its <c>data:</c> text.</summary>
/// <param name="Id">The event id, the daemon's event seq; 0 for frames sent without an id.</param>
/// <param name="Data">The data payload (a JSON-RPC notification).</param>
/// <remarks>Created by Claude</remarks>
internal sealed record SseFrame(long Id, string Data);
