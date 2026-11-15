using TessynDesktop.Models.Remote;

namespace TessynDesktop.WPF.Tests.Fakes;

/// <summary>The W4 calls of the fake client: <c>session.compact</c> and <c>session.setStreaming</c>.</summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class FakeTessynClient
{
	public Func<string, bool, bool?, SetStreamingResult>? SetStreamingHandler { get; set; }

	/// <summary>When set, answers <c>session.setStreaming</c> asynchronously (a test can hold an answer back to make two calls finish out of order).</summary>
	public Func<bool, bool?, Task<SetStreamingResult>>? SetStreamingAsyncHandler { get; set; }

	public Exception? CompactException { get; set; }

	private Task<SessionCompactResult> CompactImpl(string nodeId, string? modelId, string? effort, string? profileId) =>
		Record($"session.compact:{nodeId}:{modelId}:{effort}:{profileId}", () => CompactException != null ? throw CompactException : new SessionCompactResult(true, false));

	private Task<SetStreamingResult> SetStreamingImpl(string nodeId, bool enabled, bool? force)
	{
		if (SetStreamingAsyncHandler != null)
		{
			Record($"session.setStreaming:{nodeId}:{enabled}:{force}");
			return SetStreamingAsyncHandler(enabled, force);
		}

		return Record($"session.setStreaming:{nodeId}:{enabled}:{force}", () => SetStreamingHandler?.Invoke(nodeId, enabled, force) ?? new SetStreamingResult(enabled, false));
	}
}
