using ClaudeMaximus.Models;

namespace ClaudeMaximus.Services;

/// <remarks>Created by Claude</remarks>
public sealed class AutocompleteTriggerParser
{
	private static readonly AutocompleteTriggerModel _none = new()
	{
		Mode = AutocompleteMode.None,
		Query = string.Empty,
		TriggerStartIndex = 0,
		TriggerLength = 0
	};

	public AutocompleteTriggerModel Parse(string text, int caretIndex)
	{
		if (string.IsNullOrEmpty(text) || caretIndex <= 0 || caretIndex > text.Length)
			return _none;

		// Check for filesystem path trigger first (e.g. C:\Users\...)
		var pathResult = TryParsePath(text, caretIndex);
		if (pathResult != null)
			return pathResult;

		// Then check for # / ## triggers
		return TryParseHash(text, caretIndex);
	}

	private AutocompleteTriggerModel? TryParsePath(string text, int caretIndex)
	{
		// Scan backward from caret to find the nearest X:\ pattern on the current line.
		// The drive letter must be preceded by whitespace, newline, or be at start of text.
		// Paths may contain spaces (e.g. C:\Program Files\), so we scan for the drive pattern directly.

		// Find the start of the current line
		var lineStart = 0;
		for (var i = caretIndex - 1; i >= 0; i--)
		{
			if (text[i] == '\n' || text[i] == '\r')
			{
				lineStart = i + 1;
				break;
			}
		}

		// Scan backward from near the caret looking for X:\ pattern
		// We need at least 3 chars for X:\, so search positions where X:\ could start
		for (var i = caretIndex - 3; i >= lineStart; i--)
		{
			if (!char.IsLetter(text[i]) || text[i + 1] != ':')
				continue;

			if (text[i + 2] != '\\' && text[i + 2] != '/')
				continue;

			// Found a drive letter pattern at position i
			// Must be preceded by whitespace or be at start of text/line
			if (i > lineStart)
			{
				var before = text[i - 1];
				if (before != ' ' && before != '\t')
					continue;
			}

			var segment = text.Substring(i, caretIndex - i);
			return new AutocompleteTriggerModel
			{
				Mode = AutocompleteMode.Path,
				Query = segment,
				TriggerStartIndex = i,
				TriggerLength = caretIndex - i
			};
		}

		return null;
	}

	private static bool IsDriveLetterPath(string segment)
	{
		// Must match pattern: letter + : + backslash (at minimum X:\)
		if (segment.Length < 3)
			return false;

		return char.IsLetter(segment[0])
			&& segment[1] == ':'
			&& (segment[2] == '\\' || segment[2] == '/');
	}

	private AutocompleteTriggerModel TryParseHash(string text, int caretIndex)
	{
		var searchEnd = caretIndex;
		for (var i = caretIndex - 1; i >= 0; i--)
		{
			var ch = text[i];

			// If we hit whitespace or newline, no trigger found in this segment
			if (ch == ' ' || ch == '\t' || ch == '\n' || ch == '\r')
				return _none;

			if (ch == '#')
			{
				// Check if this '#' is preceded by another '#' (file mode)
				if (i > 0 && text[i - 1] == '#')
				{
					var triggerStart = i - 1;
					// The ## must be preceded by whitespace, newline, or be at start of text
					if (triggerStart > 0)
					{
						var before = text[triggerStart - 1];
						if (before != ' ' && before != '\t' && before != '\n' && before != '\r')
							return _none;
					}

					var query = text.Substring(i + 1, searchEnd - (i + 1));
					return new AutocompleteTriggerModel
					{
						Mode = AutocompleteMode.File,
						Query = query,
						TriggerStartIndex = triggerStart,
						TriggerLength = searchEnd - triggerStart
					};
				}

				// Single # — symbol mode
				// Must be preceded by whitespace, newline, or be at start of text
				if (i > 0)
				{
					var before = text[i - 1];
					if (before != ' ' && before != '\t' && before != '\n' && before != '\r')
						return _none;
				}

				var symbolQuery = text.Substring(i + 1, searchEnd - (i + 1));
				return new AutocompleteTriggerModel
				{
					Mode = AutocompleteMode.Symbol,
					Query = symbolQuery,
					TriggerStartIndex = i,
					TriggerLength = searchEnd - i
				};
			}
		}

		return _none;
	}
}
