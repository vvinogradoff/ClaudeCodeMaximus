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
		// Scan backward from caret to find a drive letter pattern: X:\
		// The drive letter must be preceded by whitespace/newline or be at start of text
		for (var i = caretIndex - 1; i >= 2; i--)
		{
			var ch = text[i];
			// Stop at newlines — path won't span lines
			if (ch == '\n' || ch == '\r')
				break;
		}

		// Find the start of the current "word" (backward to whitespace or SOL)
		var wordStart = caretIndex;
		for (var i = caretIndex - 1; i >= 0; i--)
		{
			var ch = text[i];
			if (ch == '\n' || ch == '\r')
				break;
			// Allow spaces in paths only if we already have a drive pattern behind us
			if (ch == ' ' || ch == '\t')
			{
				// Check if what's before is part of a path (has a backslash after the space)
				// For simplicity: find drive letter pattern from this word boundary
				var candidate = text.Substring(i + 1, caretIndex - (i + 1));
				if (IsDriveLetterPath(candidate))
				{
					wordStart = i + 1;
					// Continue scanning backward for more spaces that might be part of the path
					continue;
				}
				break;
			}
			wordStart = i;
		}

		if (wordStart >= caretIndex)
			return null;

		var segment = text.Substring(wordStart, caretIndex - wordStart);
		if (!IsDriveLetterPath(segment))
			return null;

		// Must be preceded by whitespace, newline, or be at start of text
		if (wordStart > 0)
		{
			var before = text[wordStart - 1];
			if (before != ' ' && before != '\t' && before != '\n' && before != '\r')
				return null;
		}

		return new AutocompleteTriggerModel
		{
			Mode = AutocompleteMode.Path,
			Query = segment,
			TriggerStartIndex = wordStart,
			TriggerLength = caretIndex - wordStart
		};
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
