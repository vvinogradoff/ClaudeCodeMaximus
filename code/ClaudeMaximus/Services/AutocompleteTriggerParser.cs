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

		// Scan backward from caret to find '#'
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
