using Avalonia.Input;

namespace TessynDesktop.Services;

/// <summary>
/// Resolves action names to key combos and checks if a KeyEventArgs matches an action.
/// Abstracts platform differences (Cmd vs Ctrl).
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface IKeyBindingService
{
	/// <summary>
	/// Checks if the given key event matches any binding for the specified action.
	/// </summary>
	bool Matches(string actionName, KeyEventArgs e);

	/// <summary>
	/// Gets the display string for the specified action's binding(s).
	/// </summary>
	string GetDisplayString(string actionName);

	/// <summary>
	/// Updates the binding for an action. Persists to settings.
	/// </summary>
	void SetBinding(string actionName, string bindingString);

	/// <summary>
	/// Ensures all default bindings exist (called on startup after settings load).
	/// </summary>
	void EnsureDefaults();
}
