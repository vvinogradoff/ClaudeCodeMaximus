using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <summary>
/// Reads the activity blocks of a stream-json line that <see cref="ClaudeStreamEventParser.TryParseEvent"/> does not turn into events:
/// the thinking and tool_use blocks of an assistant message and the tool_result blocks of a user message (P1.I). Pure and defensive: a
/// line that does not fit yields nothing.
/// </summary>
/// <remarks>Created by Claude</remarks>
public static class ClaudeActivityEventParser
{
	private const string ToolUseMarker = "\"tool_use\"";
	private const string ToolResultMarker = "\"tool_result\"";
	private const string ThinkingMarker = "\"thinking\"";

	/// <summary>The activity events of <paramref name="line"/>, in block order; empty for most lines (a cheap check avoids parsing them).</summary>
	public static IReadOnlyList<ClaudeStreamEvent> Parse(string line)
	{
		if (!MayContainActivity(line))
			return [];

		try
		{
			using var doc = JsonDocument.Parse(line);
			var root = doc.RootElement;
			if (!root.TryGetProperty("type", out var typeEl) || !root.TryGetProperty("message", out var message))
				return [];

			var type = typeEl.GetString();
			if (type is not (Constants.Turns.EventTypes.Assistant or "user"))
				return [];

			if (!message.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
				return [];

			var events = new List<ClaudeStreamEvent>();
			foreach (var block in content.EnumerateArray())
				AddBlockEvent(events, block, type);

			return events;
		}
		catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
		{
			// One odd line (a block that is no object, a field of another type) must never end a read loop or abort a turn.
			return [];
		}
	}

	private static bool MayContainActivity(string line) =>
		line.Contains(ToolUseMarker, StringComparison.Ordinal)
		|| line.Contains(ToolResultMarker, StringComparison.Ordinal)
		|| line.Contains(ThinkingMarker, StringComparison.Ordinal);

	private static void AddBlockEvent(List<ClaudeStreamEvent> events, JsonElement block, string? lineType)
	{
		if (block.ValueKind != JsonValueKind.Object || !block.TryGetProperty("type", out var blockType))
			return;

		switch (blockType.GetString())
		{
			case "thinking" when lineType == Constants.Turns.EventTypes.Assistant:
				var thinking = block.TryGetProperty("thinking", out var thinkingEl) ? thinkingEl.GetString() : null;
				if (!string.IsNullOrWhiteSpace(thinking))
					events.Add(new ClaudeStreamEvent { Type = Constants.Turns.EventTypes.Thinking, Content = thinking });
				break;

			case "tool_use" when lineType == Constants.Turns.EventTypes.Assistant:
				events.Add(new ClaudeStreamEvent
				{
					Type        = Constants.Turns.EventTypes.ToolUse,
					ToolUseId   = block.TryGetProperty("id", out var idEl) ? idEl.GetString() : null,
					ToolName    = block.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null,
					ToolSummary = block.TryGetProperty("input", out var input) ? SummarizeInput(input) : null,
				});
				break;

			case "tool_result" when lineType == "user":
				events.Add(new ClaudeStreamEvent
				{
					Type        = Constants.Turns.EventTypes.ToolResult,
					ToolUseId   = block.TryGetProperty("tool_use_id", out var useEl) ? useEl.GetString() : null,
					ToolSummary = block.TryGetProperty("content", out var resultContent) ? SummarizeResult(resultContent) : null,
					ToolIsError = block.TryGetProperty("is_error", out var errEl) && errEl.ValueKind == JsonValueKind.True,
				});
				break;
		}
	}

	/// <summary>The first of the well-known input keys (command, file path, pattern, ...) that holds text, as one truncated line.</summary>
	private static string? SummarizeInput(JsonElement input)
	{
		if (input.ValueKind != JsonValueKind.Object)
			return null;

		foreach (var key in Constants.Turns.ToolSummaryInputKeys)
		{
			if (input.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String)
				return OneLine(value.GetString());
		}

		return null;
	}

	/// <summary>A tool result's content is a string or a list of text blocks; the summary is its first non-empty line.</summary>
	private static string? SummarizeResult(JsonElement content)
	{
		if (content.ValueKind == JsonValueKind.String)
			return OneLine(content.GetString());

		if (content.ValueKind != JsonValueKind.Array)
			return null;

		var text = new StringBuilder();
		foreach (var part in content.EnumerateArray())
		{
			if (part.ValueKind == JsonValueKind.Object && part.TryGetProperty("text", out var textEl) && textEl.ValueKind == JsonValueKind.String)
				text.Append(textEl.GetString()).Append('\n');
		}

		return OneLine(text.ToString());
	}

	/// <summary>The first non-blank line, trimmed and cut to <see cref="Constants.Turns.Limits.ToolSummaryMaxChars"/>; null when there is none.</summary>
	private static string? OneLine(string? text)
	{
		if (string.IsNullOrWhiteSpace(text))
			return null;

		var first = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		if (first.Length == 0)
			return null;

		var line = first[0];
		return line.Length > Constants.Turns.Limits.ToolSummaryMaxChars
			? line[..Constants.Turns.Limits.ToolSummaryMaxChars] + Constants.Turns.Ellipsis
			: line;
	}
}
