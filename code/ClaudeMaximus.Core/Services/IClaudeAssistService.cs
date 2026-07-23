using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ClaudeMaximus.Models;

namespace ClaudeMaximus.Services;

/// <summary>
/// Provides Claude-powered utility functions: title generation and semantic search
/// for session import. Uses claude CLI in print mode (FR.13.8, FR.13.9, FR.13.14).
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface IClaudeAssistService
{
	/// <summary>
	/// Generates concise titles for a batch of session summaries using the Claude CLI.
	/// Returns a mapping of session ID to generated title.
	/// Returns an empty dictionary if the CLI is unavailable or fails.
	/// <paramref name="onBatchComplete"/> is called after each batch with the titles generated so far.
	/// </summary>
	Task<Dictionary<string, string>> GenerateTitlesAsync(
		List<ClaudeSessionSummaryModel> summaries,
		Action<Dictionary<string, string>>? onBatchComplete = null,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Performs semantic search over session summaries using the Claude CLI.
	/// Returns an ordered list of matching session IDs ranked by relevance.
	/// Returns an empty list if the CLI is unavailable or fails.
	/// </summary>
	Task<List<string>> SearchSessionsAsync(
		List<ClaudeSessionSummaryModel> summaries,
		string query,
		CancellationToken cancellationToken = default);
}
