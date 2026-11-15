using TessynDesktop.Models.Remote;

namespace TessynDesktop.WPF.Tests.Fakes;

/// <summary>The W4 calls of the fake client: <c>session.compact</c> and <c>session.setStreaming</c>.</summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class FakeTessynClient
{
	public Func<string, bool, bool?, SetStreamingResult>? SetStreamingHandler { get; set; }

	public Exception? CompactException { get; set; }

	private Task<SessionCompactResult> CompactImpl(string nodeId, string? modelId, string? effort, string? profileId) =>
		Record($"session.compact:{nodeId}:{modelId}:{effort}:{profileId}", () => CompactException != null ? throw CompactException : new SessionCompactResult(true, false));

	private Task<SetStreamingResult> SetStreamingImpl(string nodeId, bool enabled, bool? force) =>
		Record($"session.setStreaming:{nodeId}:{enabled}:{force}", () => SetStreamingHandler?.Invoke(nodeId, enabled, force) ?? new SetStreamingResult(enabled, false));
}
