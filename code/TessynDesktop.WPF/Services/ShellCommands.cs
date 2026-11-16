using System.Reflection;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using TessynDesktop.WPF.State;

namespace TessynDesktop.WPF.Services;

/// <summary><see cref="IShellCommands"/>: in-memory registry; gestures honour the user's overrides of the key binding settings.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class ShellCommands : IShellCommands
{
	private readonly IClientSettingsStore _settings;
	private readonly ILogger<ShellCommands> _logger;
	private readonly KeyGestureConverter _converter = new();
	private readonly object _gate = new();
	private readonly Dictionary<string, ShellCommand> _commands = new(StringComparer.Ordinal);
	private readonly List<ShellMenuEntryModel> _entries = [];
	private GestureTable? _table;

	public ShellCommands(IClientSettingsStore settings, ILogger<ShellCommands> logger)
	{
		_settings = settings;
		_logger = logger;
		_settings.Changed += OnSettingsChanged;
	}

	/// <summary>The resolved gestures: parsed once, rebuilt when a command is registered or the key binding settings change.</summary>
	private sealed record GestureTable(Dictionary<(Key Key, ModifierKeys Modifiers), ShellCommand> Winners, IReadOnlyList<GestureConflict> Conflicts);

	private void OnSettingsChanged(object? sender, ClientSettingsChangedEventArgs e)
	{
		if (e.Section == KeyBindingSection)
			lock (_gate)
				_table = null;
	}

	private static readonly string KeyBindingSection = typeof(KeyBindingSettings).GetCustomAttribute<ClientSettingsSectionAttribute>()!.Name;

	public void Register(ShellCommand command)
	{
		lock (_gate)
		{
			if (!_commands.TryAdd(command.Id, command))
				throw new InvalidOperationException($"A command with the id '{command.Id}' is already registered");

			_table = null;
		}
	}

	public void AddMenuEntry(string menuId, string commandId, int order = 0, string? group = null)
	{
		lock (_gate)
			_entries.Add(new ShellMenuEntryModel { MenuId = menuId, CommandId = commandId, Order = order, Group = group, Sequence = _entries.Count });
	}

	public ShellCommand? Find(string commandId)
	{
		lock (_gate)
			return _commands.GetValueOrDefault(commandId);
	}

	public IReadOnlyList<ShellCommand> GetCommands()
	{
		lock (_gate)
			return [.. _commands.Values];
	}

	public IReadOnlyList<ShellMenuItemModel> BuildMenu(string menuId, object? parameter)
	{
		List<(ShellMenuEntryModel Entry, ShellCommand Command)> found;
		lock (_gate)
		{
			found = _entries.Where(e => e.MenuId == menuId && _commands.ContainsKey(e.CommandId))
				.OrderBy(e => e.Order).ThenBy(e => e.Sequence)
				.Select(e => (e, _commands[e.CommandId]))
				.ToList();
		}

		var items = new List<ShellMenuItemModel>(found.Count);
		string? previousGroup = null;
		foreach (var (entry, command) in found)
		{
			items.Add(new ShellMenuItemModel
			{
				CommandId = command.Id,
				Title = command.Title,
				Gesture = GetGesture(command.Id),
				IsEnabled = CanExecute(command, parameter),
				BeginsGroup = items.Count > 0 && entry.Group != previousGroup,
			});
			previousGroup = entry.Group;
		}

		return items;
	}

	public string? GetGesture(string commandId)
	{
		var command = Find(commandId);
		if (command == null)
			return null;

		var overrides = _settings.Get<KeyBindingSettings>().Gestures;
		if (overrides.TryGetValue(commandId, out var custom))
			return string.IsNullOrWhiteSpace(custom) ? null : custom;

		return command.DefaultGesture;
	}

	public ShellCommand? FindByGesture(Key key, ModifierKeys modifiers) =>
		GetTable().Winners.GetValueOrDefault((key, modifiers));

	public IReadOnlyList<GestureConflict> GetConflicts() => GetTable().Conflicts;

	public bool TryExecuteGesture(Key key, ModifierKeys modifiers, bool textInputFocused, bool isRepeat = false, object? focused = null)
	{
		var command = FindByGesture(key, modifiers);
		if (command == null || ShellKeyPolicy.Blocks(command, key, modifiers, textInputFocused))
			return false;

		if (command.AllowsFocus != null && !command.AllowsFocus(focused))
			return false;

		// A held key fires again and again: a command that must not repeat takes the key (so the text box does not get it) but does nothing.
		if (isRepeat && command.IgnoreRepeat)
			return true;

		object? parameter;
		try
		{
			parameter = command.ResolveParameter?.Invoke();
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Resolving the parameter of command {CommandId} failed", command.Id);
			return false;
		}

		if (!CanExecute(command, parameter))
			return false;

		_ = ExecuteAsync(command.Id, parameter);
		return true;
	}

	private GestureTable GetTable()
	{
		lock (_gate)
		{
			if (_table != null)
				return _table;

			var overrides = _settings.Get<KeyBindingSettings>().Gestures;
			var claims = new Dictionary<(Key, ModifierKeys), List<(ShellCommand Command, bool IsOverride)>>();
			foreach (var command in _commands.Values)
			{
				var isOverride = overrides.TryGetValue(command.Id, out var custom);
				var text = isOverride ? custom : command.DefaultGesture;
				if (string.IsNullOrWhiteSpace(text) || !TryParse(command.Id, text, out var gesture))
					continue;

				var slot = (gesture.Key, gesture.Modifiers);
				if (!claims.TryGetValue(slot, out var list))
					claims[slot] = list = [];

				list.Add((command, isOverride));
			}

			var winners = new Dictionary<(Key Key, ModifierKeys Modifiers), ShellCommand>();
			var conflicts = new List<GestureConflict>();
			foreach (var ((key, modifiers), list) in claims)
			{
				// The user's choice beats a default; otherwise the first registered stays (the dictionary keeps insertion order here).
				var ordered = list.OrderByDescending(c => c.IsOverride).ToList();
				winners[(key, modifiers)] = ordered[0].Command;
				if (ordered.Count > 1)
				{
					var losers = ordered.Skip(1).Select(c => c.Command.Id).ToList();
					conflicts.Add(new GestureConflict(key, modifiers, ordered[0].Command.Id, losers));
					_logger.LogWarning("Gesture {Modifiers}+{Key} is claimed by several commands; {Winner} wins over {Losers}", modifiers, key, ordered[0].Command.Id, string.Join(", ", losers));
				}
			}

			return _table = new GestureTable(winners, conflicts);
		}
	}

	public async Task<bool> ExecuteAsync(string commandId, object? parameter)
	{
		var command = Find(commandId);
		if (command == null || !CanExecute(command, parameter))
			return false;

		try
		{
			await command.ExecuteAsync(parameter).ConfigureAwait(true);
			return true;
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Command {CommandId} failed", commandId);
			return false;
		}
	}

	private bool CanExecute(ShellCommand command, object? parameter)
	{
		if (command.CanExecute == null)
			return true;

		try
		{
			return command.CanExecute(parameter);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "CanExecute of command {CommandId} failed", command.Id);
			return false;
		}
	}

	private bool TryParse(string commandId, string text, out KeyGesture gesture)
	{
		try
		{
			gesture = (KeyGesture)_converter.ConvertFromInvariantString(text)!;
			return true;
		}
		catch (Exception ex) when (ex is NotSupportedException or FormatException or ArgumentException or InvalidCastException)
		{
			_logger.LogWarning("The gesture '{Gesture}' of command {CommandId} is not valid and is ignored", text, commandId);
			gesture = null!;
			return false;
		}
	}
}
