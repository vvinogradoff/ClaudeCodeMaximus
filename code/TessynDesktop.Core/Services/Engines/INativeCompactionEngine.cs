using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <summary>
/// A session engine that compacts its own conversation (FR.33.12, <see cref="Models.Engines.EngineCapabilities.NativeCompaction"/>). Auto-Compact
/// and Compact now on its sessions call it instead of Tessyn's text rewrite on Claude; the conversation id is kept. The caller holds the
/// session's turn lock.
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface INativeCompactionEngine
{
	/// <summary>
	/// Compacts the session's conversation and writes the COMPACTION separator when the engine reports it. <c>NothingToCompact</c> when the
	/// session has no conversation on this engine. Cancelling stops it and throws <see cref="System.OperationCanceledException"/>.
	/// </summary>
	Task<SessionCompactionResultModel> CompactConversationAsync(SessionNodeModel node, CancellationToken cancellationToken);
}
