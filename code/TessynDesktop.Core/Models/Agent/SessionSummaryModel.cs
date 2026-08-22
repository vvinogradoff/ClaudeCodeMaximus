using System;

namespace TessynDesktop.Models.Agent;

/// <summary>
/// Compact session descriptor returned by the <c>list_sessions</c> MCP tool (FR.15.1) and the
/// Telegram Bridge's <c>/sessions</c> command (FR.21.8).
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed record SessionSummaryModel(
	string NodeId,
	string Name,
	string DirectoryLabel,
	string WorkingDirectory,
	bool IsRunning,
	bool IsResumable,
	string? LastPrompt,
	bool IsFavorite = false,
	DateTimeOffset? LastActivityUtc = null);
