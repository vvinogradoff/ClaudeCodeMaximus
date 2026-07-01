using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using ClaudeMaximus.Models;
using Serilog;

namespace ClaudeMaximus.Services;

/// <remarks>Created by Claude</remarks>
public sealed class ClaudeSessionImportService : IClaudeSessionImportService
{
	private static readonly ILogger _log = Log.ForContext<ClaudeSessionImportService>();

	/// <summary>Session ID → generated title cache. Survives across dialog open/close cycles.</summary>
	private readonly ConcurrentDictionary<string, string> _titleCache = new();

	public void CacheTitle(string sessionId, string title)
		=> _titleCache[sessionId] = title;

	public string? GetCachedTitle(string sessionId)
		=> _titleCache.TryGetValue(sessionId, out var title) ? title : null;

	public IReadOnlyList<ClaudeSessionSummaryModel> DiscoverSessions(string workingDirectory)
	{
		var slug = Constants.ClaudeSessions.BuildProjectSlug(workingDirectory);
		var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		var slugDir = Path.Combine(
			userProfile,
			Constants.ClaudeSessions.ClaudeHomeFolderName,
			Constants.ClaudeSessions.ProjectsFolderName,
			slug);

		if (!Directory.Exists(slugDir))
		{
			_log.Information("Import: no slug directory found at {SlugDir}", slugDir);
			return [];
		}

		var results = new List<ClaudeSessionSummaryModel>();

		foreach (var jsonlPath in Directory.GetFiles(slugDir, "*" + Constants.ClaudeSessions.SessionFileExtension))
		{
			var sessionId = Path.GetFileNameWithoutExtension(jsonlPath);
			if (string.IsNullOrEmpty(sessionId))
				continue;

			try
			{
				var summary = ExtractSummary(sessionId, jsonlPath);
				if (summary == null || summary.MessageCount == 0)
					continue;

				// Apply cached title if available
				var cached = GetCachedTitle(sessionId);
				if (cached != null)
					summary.GeneratedTitle = cached;
				results.Add(summary);
			}
			catch (Exception ex)
			{
				_log.Warning(ex, "Import: failed to extract summary from {Path}", jsonlPath);
			}
		}

		results.Sort((a, b) => b.LastUsed.CompareTo(a.LastUsed));
		_log.Information("Import: discovered {Count} sessions for slug {Slug}", results.Count, slug);
		return results;
	}

	public IReadOnlyList<SessionEntryModel> ParseJsonlSession(string jsonlPath)
		=> ParseJsonlSessionCore(jsonlPath, raw: false);

	public IReadOnlyList<SessionEntryModel> ParseJsonlSessionRaw(string jsonlPath)
		=> ParseJsonlSessionCore(jsonlPath, raw: true);

	private IReadOnlyList<SessionEntryModel> ParseJsonlSessionCore(string jsonlPath, bool raw)
	{
		var entries = new List<SessionEntryModel>();
		string? lastPlanFilePath = null;
		string? lastPlanContent = null;

		using var stream = new FileStream(jsonlPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
		using var reader = new StreamReader(stream, Encoding.UTF8);

		string? line;
		while ((line = reader.ReadLine()) != null)
		{
			if (string.IsNullOrWhiteSpace(line))
				continue;

			try
			{
				using var doc = JsonDocument.Parse(line);
				var root = doc.RootElement;

				if (!root.TryGetProperty("type", out var typeEl))
					continue;

				var type = typeEl.GetString();
				var timestamp = ParseTimestamp(root);

				switch (type)
				{
					case "user":
						ParseUserEvent(root, timestamp, entries, raw);
						break;
					case "assistant":
						ParseAssistantEvent(root, timestamp, entries,
							ref lastPlanFilePath, ref lastPlanContent);
						break;
				}
			}
			catch (JsonException ex)
			{
				_log.Debug("Import: skipping malformed JSONL line: {Error}", ex.Message);
			}
			catch (Exception ex)
			{
				_log.Debug("Import: skipping line due to error: {Error}", ex.Message);
			}
		}

		_log.Information("Import: parsed {Count} entries from {Path}", entries.Count, jsonlPath);
		return entries;
	}

	private static ClaudeSessionSummaryModel? ExtractSummary(string sessionId, string jsonlPath)
	{
		DateTimeOffset? firstTimestamp = null;
		DateTimeOffset? lastTimestamp = null;
		DateTimeOffset? lastMessageTimestamp = null;
		string? firstUserPrompt = null;
		var userPromptSamples = new List<string>();
		var messageCount = 0;

		using var stream = new FileStream(jsonlPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
		using var reader = new StreamReader(stream, Encoding.UTF8);

		string? line;
		while ((line = reader.ReadLine()) != null)
		{
			if (string.IsNullOrWhiteSpace(line))
				continue;

			try
			{
				using var doc = JsonDocument.Parse(line);
				var root = doc.RootElement;

				if (!root.TryGetProperty("type", out var typeEl))
					continue;

				var type = typeEl.GetString();
				var timestamp = ParseTimestamp(root);

				firstTimestamp ??= timestamp;
				lastTimestamp = timestamp;

				if (type is "user" or "assistant")
				{
					messageCount++;
					lastMessageTimestamp = timestamp;
				}

				if (type == "user")
				{
					var content = ExtractUserContent(root, Constants.ClaudeSessions.FirstPromptMaxLength);
					if (content != null && !IsSystemInjectedContent(content))
					{
						firstUserPrompt ??= content;
						if (userPromptSamples.Count < Constants.ClaudeSessions.PromptSamplesCount)
							userPromptSamples.Add(content);
					}
				}
			}
			catch (JsonException)
			{
				// Skip malformed lines during discovery
			}
		}

		return new ClaudeSessionSummaryModel
		{
			SessionId = sessionId,
			JsonlPath = jsonlPath,
			Created = firstTimestamp ?? DateTimeOffset.MinValue,
			LastUsed = lastMessageTimestamp ?? lastTimestamp ?? DateTimeOffset.MinValue,
			MessageCount = messageCount,
			FirstUserPrompt = firstUserPrompt,
			UserPromptSamples = userPromptSamples,
		};
	}

	private static void ParseUserEvent(JsonElement root, DateTimeOffset timestamp, List<SessionEntryModel> entries, bool raw = false)
	{
		var content = ExtractUserContent(root, maxLength: 0, raw: raw);
		if (string.IsNullOrWhiteSpace(content))
			return;

		// Skip system-injected content (XML tags, slash commands) — but not in raw mode
		if (!raw && IsSystemInjectedContent(content))
			return;

		entries.Add(new SessionEntryModel
		{
			Timestamp = timestamp,
			Role = Constants.SessionFile.RoleUser,
			Content = content,
		});
	}

	private static void ParseAssistantEvent(JsonElement root, DateTimeOffset timestamp,
		List<SessionEntryModel> entries, ref string? lastPlanFilePath, ref string? lastPlanContent)
	{
		if (!root.TryGetProperty("message", out var msg))
			return;
		if (!msg.TryGetProperty("content", out var contentArray))
			return;
		if (contentArray.ValueKind != JsonValueKind.Array)
			return;

		var textBuilder = new StringBuilder();
		var toolSummaries = new List<string>();
		var questionBlocks = new List<string>();  // AskUserQuestion rendered as markdown
		var planBlocks = new List<string>();       // ExitPlanMode rendered as markdown

		foreach (var block in contentArray.EnumerateArray())
		{
			if (!block.TryGetProperty("type", out var blockType))
				continue;

			var blockTypeStr = blockType.GetString();

			if (blockTypeStr == "text" && block.TryGetProperty("text", out var text))
			{
				textBuilder.Append(text.GetString());
			}
			else if (blockTypeStr == "tool_use")
			{
				var toolName = block.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : "Unknown";
				if (toolName == "AskUserQuestion")
				{
					var md = FormatAskUserQuestion(block);
					if (md != null)
						questionBlocks.Add(md);
				}
				else if (toolName == "Write")
				{
					// Track writes to .claude/plans/ for ExitPlanMode rendering
					CaptureWrittenPlanContent(block, ref lastPlanFilePath, ref lastPlanContent);
					var description = ExtractToolDescription(block);
					toolSummaries.Add(string.IsNullOrEmpty(description)
						? $"[Tool: {toolName}]"
						: $"[Tool: {toolName}] {description}");
				}
				else if (toolName == "ExitPlanMode")
				{
					var md = FormatExitPlanMode(lastPlanFilePath, lastPlanContent);
					if (md != null)
						planBlocks.Add(md);
					else
						toolSummaries.Add("[Tool: ExitPlanMode]");
				}
				else
				{
					var description = ExtractToolDescription(block);
					toolSummaries.Add(string.IsNullOrEmpty(description)
						? $"[Tool: {toolName}]"
						: $"[Tool: {toolName}] {description}");
				}
			}
		}

		// Add text content as ASSISTANT entry
		var textContent = textBuilder.ToString().TrimEnd();
		if (!string.IsNullOrWhiteSpace(textContent))
		{
			entries.Add(new SessionEntryModel
			{
				Timestamp = timestamp,
				Role = Constants.SessionFile.RoleAssistant,
				Content = textContent,
			});
		}

		// Add AskUserQuestion blocks as ASSISTANT entries so they get markdown rendering
		foreach (var qBlock in questionBlocks)
		{
			entries.Add(new SessionEntryModel
			{
				Timestamp = timestamp,
				Role = Constants.SessionFile.RoleAssistant,
				Content = qBlock,
			});
		}

		// Add ExitPlanMode blocks as ASSISTANT entries so they get markdown rendering
		foreach (var plan in planBlocks)
		{
			entries.Add(new SessionEntryModel
			{
				Timestamp = timestamp,
				Role = Constants.SessionFile.RoleAssistant,
				Content = plan,
			});
		}

		// Add tool use summaries as SYSTEM entries
		foreach (var summary in toolSummaries)
		{
			entries.Add(new SessionEntryModel
			{
				Timestamp = timestamp,
				Role = Constants.SessionFile.RoleSystem,
				Content = summary,
			});
		}
	}

	/// <summary>
	/// Checks whether a user message looks like system-injected content rather than
	/// a real human prompt. Filters out XML-tagged content (e.g., &lt;local-command-caveat&gt;,
	/// &lt;system-reminder&gt;) and very short slash commands.
	/// </summary>
	private static bool IsSystemInjectedContent(string content)
	{
		if (string.IsNullOrWhiteSpace(content))
			return true;

		var trimmed = content.TrimStart();

		// Messages starting with '<' are XML tags injected by the system
		if (trimmed.StartsWith('<'))
			return true;

		// Very short messages (< 5 chars) that look like slash commands
		if (trimmed.Length < 5 && trimmed.StartsWith('/'))
			return true;

		return false;
	}

	/// <summary>
	/// Extracts user message content. The user event has message.content as a plain string.
	/// </summary>
	private static string? ExtractUserContent(JsonElement root, int maxLength, bool raw = false)
	{
		if (!root.TryGetProperty("message", out var msg))
			return null;
		if (!msg.TryGetProperty("content", out var contentEl))
			return null;

		string? content = null;

		if (contentEl.ValueKind == JsonValueKind.String)
			content = contentEl.GetString();
		else if (contentEl.ValueKind == JsonValueKind.Array)
		{
			// Fallback: some versions may use array format
			var sb = new StringBuilder();
			foreach (var block in contentEl.EnumerateArray())
			{
				if (block.TryGetProperty("type", out var bt) && bt.GetString() == "text"
					&& block.TryGetProperty("text", out var text))
					sb.Append(text.GetString());
			}
			if (sb.Length > 0)
				content = sb.ToString();
		}

		if (content == null)
			return null;

		// Strip Maximus-injected instruction blocks — unless raw mode is requested
		if (!raw)
		{
			var delimIdx = content.IndexOf(Constants.Instructions.Delimiter, StringComparison.Ordinal);
			if (delimIdx >= 0)
				content = content[..delimIdx].TrimEnd();
		}

		if (maxLength > 0 && content.Length > maxLength)
			return content[..maxLength];

		return content;
	}

	/// <summary>
	/// If a Write tool_use targets a path containing ".claude/plans/", captures the file path
	/// and content for later use by ExitPlanMode rendering.
	/// </summary>
	private static void CaptureWrittenPlanContent(JsonElement block,
		ref string? lastPlanFilePath, ref string? lastPlanContent)
	{
		if (!block.TryGetProperty("input", out var input))
			return;

		var filePath = input.TryGetProperty("file_path", out var fpEl) ? fpEl.GetString() : null;
		if (string.IsNullOrEmpty(filePath))
			return;

		// Normalize separators for cross-platform matching
		var normalized = filePath.Replace('\\', '/');
		if (!normalized.Contains(".claude/plans/"))
			return;

		lastPlanFilePath = filePath;
		lastPlanContent = input.TryGetProperty("content", out var contentEl) ? contentEl.GetString() : null;
	}

	/// <summary>
	/// Formats an ExitPlanMode event by rendering the plan content as markdown.
	/// First tries cached content from a prior Write tool_use, then falls back to reading from disk.
	/// Returns null if no plan content can be resolved.
	/// </summary>
	private static string? FormatExitPlanMode(string? planFilePath, string? planContent)
	{
		// Try cached content first
		var content = planContent;

		// Fallback: try reading from disk if we have a path but no content
		if (string.IsNullOrEmpty(content) && !string.IsNullOrEmpty(planFilePath))
		{
			try
			{
				if (File.Exists(planFilePath))
					content = File.ReadAllText(planFilePath);
			}
			catch (Exception ex)
			{
				_log.Debug("Import: failed to read plan file {Path}: {Error}", planFilePath, ex.Message);
			}
		}

		if (string.IsNullOrEmpty(content))
			return null;

		var sb = new StringBuilder();
		sb.AppendLine("---");
		sb.AppendLine("**Plan**");
		sb.AppendLine("---");
		sb.AppendLine();
		sb.Append(content);
		return sb.ToString().TrimEnd();
	}

	/// <summary>
	/// Formats an AskUserQuestion tool_use block as markdown with questions and options.
	/// Returns null if the block has no parseable questions.
	/// </summary>
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

	/// <summary>
	/// Extracts a brief description from a tool_use block's input.
	/// Handles tool-specific input shapes (e.g. AskUserQuestion's questions array),
	/// then falls back to common fields like description, command, file_path, etc.
	/// </summary>
	private static string? ExtractToolDescription(JsonElement block)
	{
		if (!block.TryGetProperty("input", out var input))
			return null;
		if (input.ValueKind != JsonValueKind.Object)
			return null;

		if (input.TryGetProperty("description", out var desc))
			return desc.GetString();

		if (input.TryGetProperty("command", out var cmd))
			return Truncate(cmd.GetString(), 80);

		if (input.TryGetProperty("file_path", out var fp))
			return fp.GetString();

		// Generic fallback for other tools (WebSearch, WebFetch, Grep, etc.)
		foreach (var fieldName in new[] { "query", "prompt", "pattern", "url", "text", "skill" })
		{
			if (input.TryGetProperty(fieldName, out var val) &&
			    val.ValueKind == JsonValueKind.String)
			{
				var str = val.GetString();
				if (!string.IsNullOrEmpty(str))
					return Truncate(str, 120);
			}
		}

		return null;
	}

	private static string? Truncate(string? value, int maxLength)
	{
		if (value == null) return null;
		return value.Length > maxLength ? value[..maxLength] + "..." : value;
	}

	private static DateTimeOffset ParseTimestamp(JsonElement root)
	{
		if (root.TryGetProperty("timestamp", out var ts) && ts.ValueKind == JsonValueKind.String)
		{
			if (DateTimeOffset.TryParse(ts.GetString(), out var result))
				return result;
		}
		return DateTimeOffset.UtcNow;
	}
}
