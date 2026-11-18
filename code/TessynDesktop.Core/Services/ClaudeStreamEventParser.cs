using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <summary>
/// Parses a single stream-json line (as emitted by <c>claude --output-format stream-json</c>)
/// into a <see cref="ClaudeStreamEvent"/>. Shared between <see cref="ClaudeProcessManager"/>
/// (One-shot Mode, one process per turn) and the Streaming Session Mode process manager (FR.19,
/// one persistent process across turns) — the wire format is identical, only the process
/// lifecycle around it differs. Extracted from <c>ClaudeProcessManager</c> (Stage 2 of FR.19).
/// </summary>
/// <remarks>Created by Claude</remarks>
public static class ClaudeStreamEventParser
{
	/// <summary>
	/// All events of one stream-json line: the one <see cref="TryParseEvent"/> yields (unchanged), followed by the activity events
	/// (thinking, tool_use, tool_result) of <see cref="ClaudeActivityEventParser"/>. The process managers deliver every one of them.
	/// </summary>
	public static IReadOnlyList<ClaudeStreamEvent> ParseEvents(string line)
	{
		var primary  = TryParseEvent(line);
		var activity = ClaudeActivityEventParser.Parse(line);
		if (activity.Count == 0)
			return primary == null ? [] : [primary];

		var events = new List<ClaudeStreamEvent>(activity.Count + 1);
		if (primary != null)
			events.Add(primary);
		events.AddRange(activity);
		return events;
	}

	public static ClaudeStreamEvent? TryParseEvent(string line)
	{
		try
		{
			using var doc = JsonDocument.Parse(line);
			var root = doc.RootElement;

			if (!root.TryGetProperty("type", out var typeEl))
				return null;

			var type    = typeEl.GetString() ?? string.Empty;
			var subtype = root.TryGetProperty("subtype", out var subEl) ? subEl.GetString() : null;

			return type switch
			{
				"assistant" => ParseAssistantEvent(root, type, subtype),
				"system"    => ParseSystemEvent(root, type, subtype),
				"result"    => ParseResultEvent(root, type, subtype),
				_           => null,
			};
		}
		catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
		{
			// A line of an unexpected shape is skipped, never allowed to end the read loop.
			return null;
		}
	}

	private static ClaudeStreamEvent? ParseAssistantEvent(JsonElement root, string type, string? subtype)
	{
		if (!root.TryGetProperty("message", out var msg))
			return null;

		var content = ExtractTextContent(msg);
		if (string.IsNullOrEmpty(content))
			return null;

		var apiError = root.TryGetProperty("error", out var errorEl) && errorEl.ValueKind == JsonValueKind.String ? errorEl.GetString() : null;
		var rejected = root.TryGetProperty("quotaLimits", out var quota) && quota.ValueKind == JsonValueKind.Object
			&& quota.TryGetProperty("status", out var status) && status.ValueKind == JsonValueKind.String
			&& string.Equals(status.GetString(), Constants.LimitErrors.QuotaRejectedStatus, StringComparison.OrdinalIgnoreCase);

		return new ClaudeStreamEvent { Type = type, Subtype = subtype, Content = content, ApiError = apiError, QuotaRejected = rejected };
	}

	private static ClaudeStreamEvent ParseSystemEvent(JsonElement root, string type, string? subtype)
	{
		string? content = null;

		if (root.TryGetProperty("summary", out var summary))
			content = summary.GetString();
		else if (root.TryGetProperty("message", out var msg))
			content = msg.GetString();
		else if (subtype is "task_progress" or "task_started")
		{
			// Show live tool-use descriptions as progress feedback
			var description = root.TryGetProperty("description", out var desc) ? desc.GetString() : null;
			var tool        = root.TryGetProperty("last_tool_name", out var tn)  ? tn.GetString()   : null;
			content = tool != null ? $"[{tool}] {description}" : description;
		}

		return new ClaudeStreamEvent { Type = type, Subtype = subtype, Content = content };
	}

	private static ClaudeStreamEvent ParseResultEvent(JsonElement root, string type, string? subtype)
	{
		var sessionId = root.TryGetProperty("session_id", out var sidEl) ? sidEl.GetString() : null;
		var isError   = root.TryGetProperty("is_error", out var errEl) && errEl.GetBoolean();

		string? errorMsg = null;
		if (isError)
		{
			// Claude emits errors as an "errors" array of strings, not a single "error" property.
			if (root.TryGetProperty("errors", out var errorsArr) && errorsArr.ValueKind == JsonValueKind.Array)
			{
				var sb = new StringBuilder();
				foreach (var item in errorsArr.EnumerateArray())
				{
					if (sb.Length > 0)
						sb.Append("; ");
					sb.Append(item.GetString());
				}
				if (sb.Length > 0)
					errorMsg = sb.ToString();
			}
			// Fallback: also check singular "error" property just in case
			else if (root.TryGetProperty("error", out var errMsgEl))
				errorMsg = errMsgEl.GetString();

			// Execution-time failures (e.g. auth errors, subtype "error_during_execution") carry their
			// message in the same "result" string field used for successful output, not "errors"/"error".
			if (string.IsNullOrEmpty(errorMsg)
			    && root.TryGetProperty("result", out var resultEl) && resultEl.ValueKind == JsonValueKind.String)
				errorMsg = resultEl.GetString();
		}

		// Extract token usage and cost from the result event (FR.17.1).
		int inputTokens = 0, outputTokens = 0;
		double costUsd = 0;
		if (root.TryGetProperty("usage", out var usageEl))
		{
			if (usageEl.TryGetProperty("input_tokens", out var inEl))
				inputTokens = inEl.GetInt32();
			if (usageEl.TryGetProperty("output_tokens", out var outEl))
				outputTokens = outEl.GetInt32();
		}
		if (root.TryGetProperty("total_cost_usd", out var costEl))
			costUsd = costEl.GetDouble();

		return new ClaudeStreamEvent
		{
			Type         = type,
			Subtype      = subtype,
			SessionId    = sessionId,
			IsError      = isError,
			Content      = errorMsg,
			InputTokens  = inputTokens,
			OutputTokens = outputTokens,
			CostUsd      = costUsd,
		};
	}

	private static string? ExtractTextContent(JsonElement messageElement)
	{
		if (!messageElement.TryGetProperty("content", out var contentArray))
			return null;

		var sb = new StringBuilder();
		foreach (var block in contentArray.EnumerateArray())
		{
			if (!block.TryGetProperty("type", out var blockType))
				continue;

			var blockTypeStr = blockType.GetString();
			if (blockTypeStr == "text" && block.TryGetProperty("text", out var text))
			{
				sb.Append(text.GetString());
			}
			else if (blockTypeStr == "tool_use")
			{
				var toolName = block.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null;
				if (toolName == "AskUserQuestion")
				{
					var questionMd = FormatAskUserQuestion(block);
					if (!string.IsNullOrEmpty(questionMd))
					{
						if (sb.Length > 0) sb.AppendLine();
						sb.Append(questionMd);
					}
				}
			}
		}

		return sb.Length > 0 ? sb.ToString().TrimEnd() : null;
	}

	private static string? FormatAskUserQuestion(JsonElement block)
	{
		if (!block.TryGetProperty("input", out var input))
			return null;
		if (!input.TryGetProperty("questions", out var questions) ||
		    questions.ValueKind != JsonValueKind.Array)
			return null;

		var sb = new StringBuilder();
		foreach (var q in questions.EnumerateArray())
		{
			if (!q.TryGetProperty("question", out var questionEl))
				continue;
			var questionText = questionEl.GetString();
			if (string.IsNullOrEmpty(questionText))
				continue;

			if (sb.Length > 0)
				sb.AppendLine();

			sb.AppendLine($"**{questionText}**");

			if (q.TryGetProperty("options", out var options) &&
			    options.ValueKind == JsonValueKind.Array)
			{
				foreach (var opt in options.EnumerateArray())
				{
					var label = opt.TryGetProperty("label", out var lEl) ? lEl.GetString() : null;
					var desc  = opt.TryGetProperty("description", out var dEl) ? dEl.GetString() : null;

					if (string.IsNullOrEmpty(label))
						continue;

					sb.Append($"- **{label}**");
					if (!string.IsNullOrEmpty(desc))
						sb.Append($" — {desc}");
					sb.AppendLine();
				}
			}
		}

		return sb.Length > 0 ? sb.ToString().TrimEnd() : null;
	}
}
