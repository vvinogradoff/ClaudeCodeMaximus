using System.Windows.Input;

namespace TessynDesktop.WPF.Services;

/// <summary>
/// The command registry: key bindings and menus bind by command id, so a package adds a command and its menu entries
/// (<see cref="Constants.Menus"/>) from an <see cref="IShellModule"/> without editing any view.
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface IShellCommands
{
	/// <summary>Adds a command; a second command with the same id is a programming error and throws.</summary>
	void Register(ShellCommand command);

	/// <summary>Adds a menu entry for a command (registered before or after this call); entries order by <paramref name="order"/>, then by registration.</summary>
	/// <param name="group">Entries of one group stay together; a separator is placed where the group changes.</param>
	void AddMenuEntry(string menuId, string commandId, int order = 0, string? group = null);

	ShellCommand? Find(string commandId);

	/// <summary>The entries of a menu for this parameter. Entries whose command is unknown are left out, disabled ones are kept.</summary>
	IReadOnlyList<ShellMenuItemModel> BuildMenu(string menuId, object? parameter);

	/// <summary>The gesture in effect for a command: the user's override from the key binding settings, else the default; null when unbound.</summary>
	string? GetGesture(string commandId);

	/// <summary>The command bound to a key combination, or null.</summary>
	ShellCommand? FindByGesture(Key key, ModifierKeys modifiers);

	/// <summary>Runs a command; false when it is unknown, not applicable to the parameter or failed (the failure is logged).</summary>
	Task<bool> ExecuteAsync(string commandId, object? parameter);
}
