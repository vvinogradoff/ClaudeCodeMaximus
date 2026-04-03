using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Models;
using Serilog;

namespace TessynDesktop.Services;

/// <remarks>Created by Claude</remarks>
public sealed class ClaudeAssistService : IClaudeAssistService
{
	private static readonly ILogger _log = Log.ForContext<ClaudeAssistService>();

	private readonly IClaudeProcessManager _processManager;
	private readonly IAppSettingsService _appSettings;

	public ClaudeAssistService(IClaudeProcessManager processManager, IAppSettingsService appSettings)
	{
		_processManager = processManager;
		_appSettings = appSettings;
	}

	public async Task<Dictionary<string, string>> GenerateTitlesAsync(
		List<ClaudeSessionSummaryModel> summaries,
		Action<Dictionary<string, string>>? onBatchComplete = null,
		CancellationToken cancellationToken = default)
	{
		var allTitles = new Dictionary<string, string>();

		if (summaries.Count == 0)
			return allTitles;

		// Batch up to 20 sessions per call
		var batches = Batch(summaries, Constants.ClaudeAssist.TitleBatchSize);
		var modelFallbacks = GetModelFallbackOrder();

		foreach (var batch in batches)
		{
			if (cancellationToken.IsCancellationRequested)
				break;

			var prompt = BuildTitlePrompt(batch);
			var claudePath = _appSettings.Settings.ClaudePath;
			var success = false;

			// Try each model in fallback order
			foreach (var model in modelFallbacks)
			{
				if (cancellationToken.IsCancellationRequested)
					break;

				try
				{
					_log.Debug("GenerateTitlesAsync: sending batch of {Count} sessions, model={Model}",
						batch.Count, model ?? "default");

					var rawOutput = await _processManager.RunPrintModeAsync(
						claudePath, prompt, model,
						Constants.ClaudeAssist.TimeoutMs,
						cancellationToken);

					if (rawOutput == null)
					{
						_log.Warning("GenerateTitlesAsync: CLI returned null for model={Model}, trying next",
							model ?? "default");
						continue;
					}

					var parsed = ParseTitleResponse(rawOutput);
					if (parsed.Count > 0)
					{
						foreach (var (id, title) in parsed)
							allTitles[id] = title;
						onBatchComplete?.Invoke(allTitles);
						success = true;
						break; // Success — don't try other models
					}
				}
				catch (Exception ex)
				{
					_log.Warning(ex, "GenerateTitlesAsync: error with model={Model}", model ?? "default");
				}
			}

			if (!success)
				_log.Warning("GenerateTitlesAsync: all model fallbacks failed for batch");
		}

		return allTitles;
	}

	public async Task<List<string>> SearchSessionsAsync(
		List<ClaudeSessionSummaryModel> summaries,
		string query,
		CancellationToken cancellationToken = default)
	{
		if (summaries.Count == 0 || string.IsNullOrWhiteSpace(query))
			return [];

		var prompt = BuildSearchPrompt(summaries, query);
		var claudePath = _appSettings.Settings.ClaudePath;
		var modelFallbacks = GetModelFallbackOrder();

		// Try each model in fallback order
		foreach (var model in modelFallbacks)
		{
			if (cancellationToken.IsCancellationRequested)
				return [];

			try
			{
				_log.Debug("SearchSessionsAsync: query={Query}, sessions={Count}, model={Model}",
					query, summaries.Count, model ?? "default");

				var rawOutput = await _processManager.RunPrintModeAsync(
					claudePath, prompt, model,
					Constants.ClaudeAssist.TimeoutMs,
					cancellationToken);

				if (rawOutput == null)
				{
					_log.Warning("SearchSessionsAsync: CLI returned null for model={Model}, trying next",
						model ?? "default");
					continue;
				}

				var result = ParseSearchResponse(rawOutput);
				if (result.Count > 0)
					return result;
			}
			catch (Exception ex)
			{
				_log.Warning(ex, "SearchSessionsAsync: error with model={Model}", model ?? "default");
			}
		}

		_log.Warning("SearchSessionsAsync: all model fallbacks failed");
		return [];
	}

	/// <summary>
	/// Returns the ordered list of models to try for assist calls.
	/// Per FR.13.14: haiku first, then user's FR.12 selection, then CLI default (null).
	/// </summary>
	private List<string?> GetModelFallbackOrder()
	{
		return GetModelFallbackOrderFromIndex(_appSettings.Settings.SelectedModelIndex);
	}

	/// <summary>
	/// Returns ordered model candidates. Testable static method.
	/// </summary>
	public static List<string?> GetModelFallbackOrderFromIndex(int selectedModelIndex)
	{
		var models = new List<string?>();

		// 1. Always try haiku first (preferred for speed/cost)
		models.Add(Constants.ClaudeAssist.PreferredModel);

		// 2. User's selected model (if different from haiku and non-default)
		var userModel = selectedModelIndex switch
		{
			1 => "opus",
			2 => "sonnet",
			3 => "haiku", // same as preferred, skip
			_ => null,    // default: no model flag
		};

		if (userModel != null && userModel != Constants.ClaudeAssist.PreferredModel)
			models.Add(userModel);

		// 3. No model flag (CLI decides)
		models.Add(null);

		return models;
	}

	private static string BuildTitlePrompt(List<ClaudeSessionSummaryModel> batch)
	{
		var sb = new StringBuilder();
		sb.AppendLine("Generate specific, descriptive 4-8 word titles for the following Claude Code sessions.");
		sb.AppendLine("Each title must capture WHAT was actually worked on — mention specific features, files, APIs, bugs, or technologies.");
		sb.AppendLine("BAD titles: 'Local Command Check', 'Code Review Session', 'Debug Application Issue' (too vague).");
		sb.AppendLine("GOOD titles: 'Fix Auth Endpoint 500 Error', 'Add Redis Cache to User API', 'Refactor Lease Rate Calculation' (specific).");
		sb.AppendLine("Use the user prompts below to understand the actual work done.");
		sb.AppendLine("Respond with ONLY a JSON object mapping session IDs to titles. No markdown, no explanation.");
		sb.AppendLine("Example: {\"abc-123\": \"Fix Auth Endpoint 500 Error\", \"def-456\": \"Add Redis Cache to User API\"}");
		sb.AppendLine();
		sb.AppendLine("Sessions:");

		foreach (var summary in batch)
		{
			sb.AppendLine($"- ID: {summary.SessionId}");
			sb.AppendLine($"  Messages: {summary.MessageCount}");

			if (summary.UserPromptSamples.Count > 0)
			{
				for (var i = 0; i < summary.UserPromptSamples.Count; i++)
				{
					var label = i == 0 ? "First prompt" : $"Prompt {i + 1}";
					var prompt = summary.UserPromptSamples[i];
					sb.AppendLine($"  {label}: {prompt}");
				}
			}
			else if (summary.FirstUserPrompt != null)
			{
				sb.AppendLine($"  First prompt: {summary.FirstUserPrompt}");
			}
			else
			{
				sb.AppendLine("  (empty session)");
			}

			sb.AppendLine();
		}

		return sb.ToString();
	}

	private static string BuildSearchPrompt(List<ClaudeSessionSummaryModel> summaries, string query)
	{
		var sb = new StringBuilder();
		sb.AppendLine("Search the following Claude Code sessions for relevance to the query.");
		sb.AppendLine("Respond with ONLY a JSON array of session IDs ranked by relevance (most relevant first).");
		sb.AppendLine("Only include sessions that are relevant. No markdown, no explanation.");
		sb.AppendLine("Example: [\"abc-123\", \"def-456\"]");
		sb.AppendLine();
		sb.AppendLine($"Query: {query}");
		sb.AppendLine();
		sb.AppendLine("Sessions:");

		foreach (var summary in summaries)
		{
			var preview = summary.GeneratedTitle ?? summary.FirstUserPrompt ?? "(empty)";
			if (preview.Length > 200)
				preview = preview[..200] + "...";
			sb.AppendLine($"- ID: {summary.SessionId}");
			sb.AppendLine($"  Title: {preview}");
			sb.AppendLine($"  Messages: {summary.MessageCount}");
			sb.AppendLine();
		}

		return sb.ToString();
	}

	/// <summary>
	/// Parses a title generation response. Expects a JSON object mapping session IDs to titles.
	/// Handles wrapped JSON output from --output-format json.
	/// </summary>
	public static Dictionary<string, string> ParseTitleResponse(string rawOutput)
	{
		var result = new Dictionary<string, string>();

		try
		{
			var json = ExtractJsonContent(rawOutput);
			if (json == null)
				return result;

			using var doc = JsonDocument.Parse(json);

			// Response could be a plain object or wrapped in {"result":"..."} from --output-format json
			var root = doc.RootElement;

			if (root.ValueKind == JsonValueKind.Object)
			{
				// Check if it's a wrapper with a "result" string that contains the actual JSON
				if (root.TryGetProperty("result", out var resultEl) && resultEl.ValueKind == JsonValueKind.String)
				{
					var innerJson = ExtractJsonFromText(resultEl.GetString());
					if (innerJson != null)
						return ParseJsonObjectAsTitles(innerJson);
				}

				// Direct object: treat each property as sessionId -> title
				foreach (var prop in root.EnumerateObject())
				{
					if (prop.Value.ValueKind == JsonValueKind.String)
						result[prop.Name] = prop.Value.GetString() ?? string.Empty;
				}
			}
		}
		catch (JsonException ex)
		{
			_log.Debug("ParseTitleResponse: JSON parse error: {Error}", ex.Message);
		}

		return result;
	}

	/// <summary>
	/// Parses a search response. Expects a JSON array of session IDs.
	/// </summary>
	public static List<string> ParseSearchResponse(string rawOutput)
	{
		var result = new List<string>();

		try
		{
			var json = ExtractJsonContent(rawOutput);
			if (json == null)
				return result;

			using var doc = JsonDocument.Parse(json);
			var root = doc.RootElement;

			if (root.ValueKind == JsonValueKind.Array)
			{
				foreach (var item in root.EnumerateArray())
				{
					if (item.ValueKind == JsonValueKind.String)
					{
						var id = item.GetString();
						if (!string.IsNullOrEmpty(id))
							result.Add(id);
					}
				}
			}
			else if (root.ValueKind == JsonValueKind.Object)
			{
				// Wrapped response: {"result":"[\"abc\",\"def\"]"}
				if (root.TryGetProperty("result", out var resultEl) && resultEl.ValueKind == JsonValueKind.String)
				{
					var innerJson = ExtractJsonFromText(resultEl.GetString());
					if (innerJson != null)
						return ParseJsonArrayAsIds(innerJson);
				}
			}
		}
		catch (JsonException ex)
		{
			_log.Debug("ParseSearchResponse: JSON parse error: {Error}", ex.Message);
		}

		return result;
	}

	private static Dictionary<string, string> ParseJsonObjectAsTitles(string json)
	{
		var result = new Dictionary<string, string>();
		try
		{
			using var doc = JsonDocument.Parse(json);
			foreach (var prop in doc.RootElement.EnumerateObject())
			{
				if (prop.Value.ValueKind == JsonValueKind.String)
					result[prop.Name] = prop.Value.GetString() ?? string.Empty;
			}
		}
		catch (JsonException)
		{
			// Ignore parse errors on inner JSON
		}
		return result;
	}

	private static List<string> ParseJsonArrayAsIds(string json)
	{
		var result = new List<string>();
		try
		{
			using var doc = JsonDocument.Parse(json);
			foreach (var item in doc.RootElement.EnumerateArray())
			{
				if (item.ValueKind == JsonValueKind.String)
				{
					var id = item.GetString();
					if (!string.IsNullOrEmpty(id))
						result.Add(id);
				}
			}
		}
		catch (JsonException)
		{
			// Ignore parse errors on inner JSON
		}
		return result;
	}

	/// <summary>
	/// Extracts JSON content from CLI output that may contain extra text.
	/// Looks for the first { or [ and matches to its closing bracket.
	/// </summary>
	private static string? ExtractJsonContent(string? text)
	{
		if (string.IsNullOrWhiteSpace(text))
			return null;

		// Try to parse the whole text as JSON first
		text = text.Trim();
		try
		{
			using var doc = JsonDocument.Parse(text);
			return text;
		}
		catch (JsonException)
		{
			// Not valid JSON as-is, try to extract
		}

		return ExtractJsonFromText(text);
	}

	/// <summary>
	/// Finds and extracts the first JSON object or array from text that may contain surrounding prose.
	/// </summary>
	private static string? ExtractJsonFromText(string? text)
	{
		if (text == null)
			return null;

		// Find first { or [
		var objStart = text.IndexOf('{');
		var arrStart = text.IndexOf('[');

		int start;
		char openChar, closeChar;

		if (objStart >= 0 && (arrStart < 0 || objStart < arrStart))
		{
			start = objStart;
			openChar = '{';
			closeChar = '}';
		}
		else if (arrStart >= 0)
		{
			start = arrStart;
			openChar = '[';
			closeChar = ']';
		}
		else
			return null;

		// Find matching close bracket
		var depth = 0;
		var inString = false;
		var escape = false;

		for (var i = start; i < text.Length; i++)
		{
			var c = text[i];

			if (escape)
			{
				escape = false;
				continue;
			}

			if (c == '\\' && inString)
			{
				escape = true;
				continue;
			}

			if (c == '"')
			{
				inString = !inString;
				continue;
			}

			if (inString)
				continue;

			if (c == openChar)
				depth++;
			else if (c == closeChar)
			{
				depth--;
				if (depth == 0)
					return text[start..(i + 1)];
			}
		}

		return null;
	}

	private static List<List<T>> Batch<T>(List<T> source, int batchSize)
	{
		var batches = new List<List<T>>();
		for (var i = 0; i < source.Count; i += batchSize)
			batches.Add(source.GetRange(i, Math.Min(batchSize, source.Count - i)));
		return batches;
	}
}
