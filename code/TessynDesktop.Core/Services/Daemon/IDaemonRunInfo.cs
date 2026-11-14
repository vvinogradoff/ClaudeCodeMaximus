namespace TessynDesktop.Services;

/// <summary>Identity of this daemon run: shared by <c>client.hello</c>, the SSE resume check and <c>daemon.json</c>.</summary>
/// <remarks>Created by Claude</remarks>
public interface IDaemonRunInfo
{
	/// <summary>A new GUID per process run; changes when the app restarts.</summary>
	string RunId { get; }

	/// <summary>Process id of the daemon host.</summary>
	int ProcessId { get; }
}
