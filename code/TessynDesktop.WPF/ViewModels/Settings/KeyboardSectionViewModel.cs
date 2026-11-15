using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.State;

namespace TessynDesktop.WPF.ViewModels.Settings;

/// <summary>
/// The Keyboard shortcuts tab: every command of the registry with the key combination in effect, edited as text in the notation the registry reads (so values imported from
/// Avalonia stay valid). Only a combination that differs from the command's default is stored; an empty one unbinds the command. Two commands on one combination are flagged
/// (the registry resolves the clash, the editor does not forbid it).
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class KeyboardSectionViewModel : SettingsSectionViewModel, ISettingsEditor
{
	private readonly IClientSettingsStore _store;
	private readonly IShellCommands _commands;

	public KeyboardSectionViewModel(IClientSettingsStore store, IShellCommands commands)
	{
		_store = store;
		_commands = commands;
		Read();
	}

	public override string Title => "Keyboard shortcuts";

	public ObservableCollection<KeyBindingRowViewModel> Rows { get; } = [];

	public bool HasChanges
	{
		get
		{
			var gestures = _store.Get<KeyBindingSettings>().Gestures;
			return Rows.Any(r => Effective(gestures, r) != r.Text.Trim());
		}
	}

	public Task<bool> SaveAsync()
	{
		ClearMessages();
		Validate();
		var bad = Rows.FirstOrDefault(r => r.Text.Trim().Length > 0 && !GestureText.TryParse(r.Text, out _, out _));
		if (bad != null)
		{
			ErrorText = string.Format(Constants.Settings.SaveFailedFormat, string.Format(Constants.Settings.GestureInvalidFormat, bad.Text.Trim(), bad.Title));
			return Task.FromResult(false);
		}

		_store.Update<KeyBindingSettings>(settings =>
		{
			// Keep the entries of commands this list does not show (a later package's, or an import's).
			foreach (var row in Rows)
			{
				var text = row.Text.Trim();
				if (string.Equals(text, row.DefaultGesture, StringComparison.OrdinalIgnoreCase))
					settings.Gestures.Remove(row.CommandId);
				else
					settings.Gestures[row.CommandId] = text;
			}
		});
		StatusText = Constants.Settings.Saved;
		return Task.FromResult(true);
	}

	[RelayCommand]
	public Task ReloadAsync()
	{
		ClearMessages();
		Read();
		return Task.CompletedTask;
	}

	/// <summary>Puts a command back to its default combination in the editor.</summary>
	[RelayCommand]
	public void ResetRow(KeyBindingRowViewModel? row)
	{
		if (row == null)
			return;

		row.Text = row.DefaultGesture;
		Validate();
	}

	/// <summary>Marks rows with an invalid or a shared combination; called after every edit by the view and before a save.</summary>
	[RelayCommand]
	public void Validate()
	{
		var seen = new Dictionary<(System.Windows.Input.Key, System.Windows.Input.ModifierKeys), KeyBindingRowViewModel>();
		foreach (var row in Rows)
		{
			row.Problem = null;
			var text = row.Text.Trim();
			if (text.Length == 0)
				continue;

			if (!GestureText.TryParse(text, out var key, out var modifiers))
			{
				row.Problem = string.Format(Constants.Settings.GestureInvalidFormat, text, row.Title);
				continue;
			}

			if (seen.TryGetValue((key, modifiers), out var first))
			{
				row.Problem = string.Format(Constants.Settings.GestureConflictFormat, first.Title);
				first.Problem ??= string.Format(Constants.Settings.GestureConflictFormat, row.Title);
			}
			else
			{
				seen[(key, modifiers)] = row;
			}
		}
	}

	/// <summary>Reads the registry and the stored overrides into the editor (also after an import).</summary>
	public void Read()
	{
		var gestures = _store.Get<KeyBindingSettings>().Gestures;
		Rows.Clear();
		foreach (var command in _commands.GetCommands().OrderBy(c => c.Title, StringComparer.CurrentCultureIgnoreCase))
		{
			var row = new KeyBindingRowViewModel(command.Id, command.Title, command.DefaultGesture, string.Empty);
			row.Text = Effective(gestures, row);
			Rows.Add(row);
		}

		Validate();
	}

	private static string Effective(Dictionary<string, string> gestures, KeyBindingRowViewModel row) =>
		gestures.TryGetValue(row.CommandId, out var custom) ? custom.Trim() : row.DefaultGesture;
}
