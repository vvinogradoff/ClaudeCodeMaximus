using System.Windows.Input;

namespace TessynDesktop.WPF.Services;

/// <summary>
/// What one module registers, held back until its <see cref="IShellModule.Register"/> completes: a module that throws half-way leaves
/// nothing behind. Reads and execution go to the real registries; a duplicate command id is caught here, as the real registry would.
/// </summary>
/// <remarks>Created by Claude</remarks>
internal sealed class StagedShellRegistrations : IShellSlots, IShellCommands
{
	private readonly IShellSlots _slots;
	private readonly IShellCommands _commands;
	private readonly List<ShellSlotContribution> _stagedSlots = [];
	private readonly List<ShellCommand> _stagedCommands = [];
	private readonly List<(string MenuId, string CommandId, int Order, string? Group)> _stagedEntries = [];

	public StagedShellRegistrations(IShellSlots slots, IShellCommands commands)
	{
		_slots = slots;
		_commands = commands;
	}

	/// <summary>Moves everything staged into the real registries.</summary>
	public void Commit()
	{
		foreach (var command in _stagedCommands)
			_commands.Register(command);

		foreach (var entry in _stagedEntries)
			_commands.AddMenuEntry(entry.MenuId, entry.CommandId, entry.Order, entry.Group);

		foreach (var slot in _stagedSlots)
			_slots.Add(slot);
	}

	public void Add(ShellSlotContribution contribution) => _stagedSlots.Add(contribution);

	public IReadOnlyList<ShellSlotContribution> For(string slotId) => _slots.For(slotId).Concat(_stagedSlots.Where(c => c.SlotId == slotId)).OrderBy(c => c.Order).ToList();

	public void Register(ShellCommand command)
	{
		if (_commands.Find(command.Id) != null || _stagedCommands.Any(c => c.Id == command.Id))
			throw new InvalidOperationException($"A command with the id '{command.Id}' is already registered");

		_stagedCommands.Add(command);
	}

	public void AddMenuEntry(string menuId, string commandId, int order = 0, string? group = null) => _stagedEntries.Add((menuId, commandId, order, group));

	public ShellCommand? Find(string commandId) => _stagedCommands.FirstOrDefault(c => c.Id == commandId) ?? _commands.Find(commandId);

	public IReadOnlyList<ShellMenuItemModel> BuildMenu(string menuId, object? parameter) => _commands.BuildMenu(menuId, parameter);

	public string? GetGesture(string commandId) => _commands.GetGesture(commandId);

	public ShellCommand? FindByGesture(Key key, ModifierKeys modifiers) => _commands.FindByGesture(key, modifiers);

	public bool TryExecuteGesture(Key key, ModifierKeys modifiers, bool textInputFocused, bool isRepeat = false) => _commands.TryExecuteGesture(key, modifiers, textInputFocused, isRepeat);

	public IReadOnlyList<GestureConflict> GetConflicts() => _commands.GetConflicts();

	public Task<bool> ExecuteAsync(string commandId, object? parameter) => _commands.ExecuteAsync(commandId, parameter);
}
