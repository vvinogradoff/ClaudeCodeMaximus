using System.ComponentModel;
using System.Reflection;
using TessynDesktop.WPF.State;

namespace TessynDesktop.WPF.Services;

/// <summary>
/// The tooltips of the Focus and Session buttons (FR.6.23.7, FR.6.23.8): the text plus the key of the command in effect, which follows a change made in Settings → Keyboard
/// shortcuts. Without a command registry (tests, tools) the tooltips carry no key.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class ShellButtonHints : INotifyPropertyChanged
{
	private static readonly string KeyBindingSection = typeof(KeyBindingSettings).GetCustomAttribute<ClientSettingsSectionAttribute>()!.Name;

	private readonly IShellCommands? _commands;

	public ShellButtonHints()
	{
	}

	public ShellButtonHints(IShellCommands commands, IClientSettingsStore settings)
	{
		_commands = commands;
		settings.Changed += OnSettingsChanged;
	}

	public event PropertyChangedEventHandler? PropertyChanged;

	/// <summary>"Show what needs your attention (Ctrl+2)".</summary>
	public string FocusToolTip => With(Constants.ShellLayout.FocusButtonToolTip, Constants.ShellLayout.ShowFocusCommandId);

	/// <summary>"Show the session (Ctrl+1)".</summary>
	public string SessionToolTip => With(Constants.ShellLayout.SessionButtonToolTip, Constants.ShellLayout.ShowSessionCommandId);

	private string With(string text, string commandId)
	{
		var gesture = _commands?.GetGesture(commandId);
		return string.IsNullOrWhiteSpace(gesture) ? text : string.Format(Constants.ShellLayout.ToolTipKeyFormat, text, gesture);
	}

	private void OnSettingsChanged(object? sender, ClientSettingsChangedEventArgs e)
	{
		if (e.Section != KeyBindingSection)
			return;

		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FocusToolTip)));
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SessionToolTip)));
	}
}
