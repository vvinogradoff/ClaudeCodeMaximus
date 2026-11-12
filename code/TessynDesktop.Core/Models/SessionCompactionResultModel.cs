namespace TessynDesktop.Models;

/// <summary>Result of <see cref="Services.ISessionCompactionService"/>.</summary>
/// <param name="Outcome">How the run ended.</param>
/// <param name="Error">The CLI/rewrite error text when <paramref name="Outcome"/> is Failed; null otherwise (also null for an empty result).</param>
/// <param name="SystemMessage">The SYSTEM line describing the outcome (Constants.Compaction texts); never written to the file by the service.</param>
/// <param name="SessionDetached">True when the live Claude session id was moved to PriorClaudeSessionIds (success with a session id present).</param>
/// <remarks>Created by Claude</remarks>
public sealed record SessionCompactionResultModel(
	SessionCompactionOutcome Outcome,
	string? Error,
	string SystemMessage,
	bool SessionDetached);
