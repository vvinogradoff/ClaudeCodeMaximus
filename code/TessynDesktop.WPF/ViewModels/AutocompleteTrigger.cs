namespace TessynDesktop.WPF.ViewModels;

/// <summary>
/// A cheap check on the client before the daemon is asked: only text that could contain a trigger (<c>#</c>, <c>##</c>, or a drive path such as
/// <c>C:\</c> before the caret) is worth a round trip. The daemon's parser has the final say; this only spares it the keystrokes that never
/// could trigger.
/// </summary>
/// <remarks>Created by Claude</remarks>
public static class AutocompleteTrigger
{
	public static bool MayTrigger(string text, int caret)
	{
		if (string.IsNullOrEmpty(text) || caret <= 0 || caret > text.Length)
			return false;

		// A '#' in the word before the caret.
		for (var i = caret - 1; i >= 0; i--)
		{
			var ch = text[i];
			if (ch is ' ' or '\t' or '\n' or '\r')
				break;

			if (ch == '#')
				return true;
		}

		// A drive letter, a colon and a separator on the current line before the caret.
		for (var i = caret - 1; i >= 2; i--)
		{
			if (text[i] is '\n' or '\r')
				break;

			if (text[i] is '\\' or '/' && text[i - 1] == ':' && char.IsLetter(text[i - 2]))
				return true;
		}

		return false;
	}
}
