using System;
using System.Collections.Generic;

namespace TessynDesktop.Models;

/// <remarks>Created by Claude</remarks>
public sealed class ClaudeSessionSummaryModel
{
	public required string SessionId { get; init; }
	public required string JsonlPath { get; init; }
	public DateTimeOffset Created { get; init; }
	public DateTimeOffset LastUsed { get; init; }
	public int MessageCount { get; init; }
	public string? FirstUserPrompt { get; init; }

	/// <summary>First few user prompts for richer title generation context (up to 3, each truncated to 500 chars).</summary>
	public IReadOnlyList<string> UserPromptSamples { get; init; } = [];

	/// <summary>Claude-generated title, populated asynchronously. Null until generated.</summary>
	public string? GeneratedTitle { get; set; }

	/// <summary>Original project directory path where the JSONL lives. Set for cross-project daemon results.</summary>
	public string? OriginalProjectPath { get; set; }

	/// <summary>Project slug from the daemon (e.g. "-Users-aisling-...-tessyn").</summary>
	public string? ProjectSlug { get; set; }
}
