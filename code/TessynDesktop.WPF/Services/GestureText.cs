using System.Windows.Input;

namespace TessynDesktop.WPF.Services;

/// <summary>Reads key combinations the way the command registry does (WPF notation: <c>Ctrl+Shift+N</c>, <c>F2</c>, <c>Ctrl+OemComma</c>), for the Settings editor and the Avalonia import.</summary>
/// <remarks>Created by Claude</remarks>
public static class GestureText
{
	private static readonly KeyGestureConverter Converter = new();

	/// <summary>True when <paramref name="text"/> is a key combination the registry can bind.</summary>
	public static bool TryParse(string? text, out Key key, out ModifierKeys modifiers)
	{
		key = Key.None;
		modifiers = ModifierKeys.None;
		if (string.IsNullOrWhiteSpace(text))
			return false;

		try
		{
			if (Converter.ConvertFromInvariantString(text.Trim()) is not KeyGesture gesture || gesture.Key == Key.None)
				return false;

			key = gesture.Key;
			modifiers = gesture.Modifiers;
			return true;
		}
		catch (Exception ex) when (ex is NotSupportedException or ArgumentException or FormatException or InvalidOperationException)
		{
			return false;
		}
	}

	/// <summary>
	/// Converts one Avalonia key combination (<c>Cmd+I</c>, <c>Ctrl+Enter</c>, <c>Escape</c>; several separated by commas) into WPF notation: <c>Cmd</c> and <c>Meta</c> become <c>Ctrl</c>
	/// (the macOS shortcuts are dropped, decision A8) and the first combination WPF accepts is returned; null when none is.
	/// </summary>
	public static string? FromAvalonia(string? binding)
	{
		if (string.IsNullOrWhiteSpace(binding))
			return null;

		foreach (var combo in binding.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
		{
			var parts = combo.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
				.Select(p => p.ToLowerInvariant() is "cmd" or "meta" or "control" ? "Ctrl" : p)
				.ToList();
			var text = string.Join('+', parts);
			if (TryParse(text, out _, out _))
				return text;
		}

		return null;
	}
}
