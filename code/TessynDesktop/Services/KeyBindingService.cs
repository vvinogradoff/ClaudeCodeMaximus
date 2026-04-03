using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Avalonia.Input;
using Serilog;

namespace TessynDesktop.Services;

/// <summary>
/// Resolves action names to key combos and checks if KeyEventArgs matches an action.
/// Handles platform detection: on macOS, "Cmd" maps to KeyModifiers.Meta;
/// on Windows/Linux, "Ctrl" maps to KeyModifiers.Control.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class KeyBindingService : IKeyBindingService
{
	private static readonly ILogger _log = Log.ForContext<KeyBindingService>();
	private readonly IAppSettingsService _appSettings;
	private static readonly bool _isMac = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

	public KeyBindingService(IAppSettingsService appSettings)
	{
		_appSettings = appSettings;
	}

	public bool Matches(string actionName, KeyEventArgs e)
	{
		var bindings = _appSettings.Settings.KeyBindings.Bindings;
		if (!bindings.TryGetValue(actionName, out var bindingStr))
			return false;

		var combos = bindingStr.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
		foreach (var combo in combos)
		{
			if (MatchesSingleCombo(combo, e))
				return true;
		}

		return false;
	}

	public string GetDisplayString(string actionName)
	{
		var bindings = _appSettings.Settings.KeyBindings.Bindings;
		if (bindings.TryGetValue(actionName, out var bindingStr))
			return bindingStr;
		return string.Empty;
	}

	public void SetBinding(string actionName, string bindingString)
	{
		_appSettings.Settings.KeyBindings.Bindings[actionName] = bindingString;
		_appSettings.Save();
	}

	public void EnsureDefaults()
	{
		_appSettings.Settings.KeyBindings.EnsureDefaults();
	}

	/// <summary>
	/// Parses a single combo string like "Cmd+I", "Ctrl+W", or "Escape" and checks
	/// if it matches the given KeyEventArgs.
	/// </summary>
	private static bool MatchesSingleCombo(string combo, KeyEventArgs e)
	{
		var parts = combo.Split('+', StringSplitOptions.TrimEntries);
		var expectedModifiers = KeyModifiers.None;
		Key expectedKey = Key.None;

		foreach (var part in parts)
		{
			var lower = part.ToLowerInvariant();
			switch (lower)
			{
				case "cmd" or "meta":
					expectedModifiers |= _isMac ? KeyModifiers.Meta : KeyModifiers.Control;
					break;
				case "ctrl" or "control":
					expectedModifiers |= KeyModifiers.Control;
					break;
				case "shift":
					expectedModifiers |= KeyModifiers.Shift;
					break;
				case "alt":
					expectedModifiers |= KeyModifiers.Alt;
					break;
				case "escape" or "esc":
					expectedKey = Key.Escape;
					break;
				default:
					if (Enum.TryParse<Key>(part, ignoreCase: true, out var parsed))
						expectedKey = parsed;
					else
						_log.Debug("KeyBindingService: unrecognised key '{Key}' in combo '{Combo}'", part, combo);
					break;
			}
		}

		if (expectedKey == Key.None)
			return false;

		return e.Key == expectedKey && e.KeyModifiers == expectedModifiers;
	}
}
