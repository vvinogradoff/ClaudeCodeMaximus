using System;

namespace ClaudeMaximus.Models;

/// <summary>
/// A single full-text search hit from <see cref="Services.IClaudeSessionIndexService"/>, at
/// message granularity. Replaces the NodeJS tessyn daemon's TessynSearchResult (Stage 6) — the
/// index is now built and owned in-process by <c>ClaudeSessionIndexService</c>.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class SessionSearchResultModel
{
	/// <summary>Stable Claude CLI session identifier (the JSONL file name, without extension).</summary>
	public required string ExternalId { get; init; }

	/// <summary>The Claude CLI project slug this session's JSONL lives under (e.g. "-Users-x-my-project").</summary>
	public required string ProjectSlug { get; init; }

	/// <summary>Zero-based index of the matching message within its session.</summary>
	public int MessageIndex { get; init; }

	/// <summary>The matching message's text content.</summary>
	public required string Content { get; init; }

	/// <summary>"user" or "assistant".</summary>
	public required string Role { get; init; }

	public DateTimeOffset Timestamp { get; init; }

	/// <summary>The session's generated/persisted title, if one exists.</summary>
	public string? SessionTitle { get; init; }

	/// <summary>FTS5 bm25 relevance score — lower is more relevant.</summary>
	public double Rank { get; init; }
}
