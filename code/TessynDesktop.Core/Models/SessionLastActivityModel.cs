using System;

namespace TessynDesktop.Models;

/// <summary>Last-prompt, last-response, profile and draft timestamps of one session file, as read from disk.</summary>
/// <remarks>Created by Claude</remarks>
public sealed record SessionLastActivityModel(
	string FileName,
	DateTimeOffset? LastPromptTimestamp,
	DateTimeOffset? LastResponseTimestamp,
	string? ProfileName,
	DateTimeOffset? LastDraftEditTimestamp);
