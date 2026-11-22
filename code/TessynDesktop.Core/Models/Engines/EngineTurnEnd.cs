using System;

namespace TessynDesktop.Models.Engines;

/// <summary>
/// The result that ends a turn (or one attempt of it). Exactly one of: a success, a failure with <see cref="ErrorText"/>, or a failure without
/// text. A transient failure the engine retries by itself is never reported here (see <see cref="EngineRetryKind"/>).
/// </summary>
/// <param name="Success">The turn completed.</param>
/// <param name="ErrorText">The error the turn failed with; null on success or when the engine gave no text.</param>
/// <param name="IsAuthError">The error is an invalid-credentials error (FR.12.16).</param>
/// <param name="IsLimitError">The error is a usage or rate-limit error (FR.1.29).</param>
/// <param name="ConversationId">The engine's conversation id the turn ended on, when the result carries it. The engine has stored it already.</param>
/// <param name="InputTokens">Input tokens of the turn (FR.17.1); 0 when not reported.</param>
/// <param name="OutputTokens">Output tokens of the turn (FR.17.1); 0 when not reported.</param>
/// <param name="CostUsd">Cost in USD (FR.17.1); 0 when not reported.</param>
/// <param name="Timestamp">When the engine reported it.</param>
/// <param name="ResultText">
/// The worker result of the turn (<c>TurnResultModel.AssistantText</c>, FR.15) when the engine composes it itself (Codex: the agentMessage texts
/// separated by a blank line, FR.33.11); null keeps the assistant texts as they were reported.
/// </param>
/// <remarks>Created by Claude</remarks>
public sealed record EngineTurnEnd(
	bool Success,
	string? ErrorText,
	bool IsAuthError,
	bool IsLimitError,
	string? ConversationId,
	int InputTokens,
	int OutputTokens,
	double CostUsd,
	DateTimeOffset Timestamp,
	string? ResultText = null);
