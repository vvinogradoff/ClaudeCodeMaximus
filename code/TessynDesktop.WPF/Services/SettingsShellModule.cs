namespace TessynDesktop.WPF.Services;

/// <summary>W6: the command <c>app.openSettings</c> (Ctrl+, by default, editable in the Keyboard shortcuts tab) and its entry in the hamburger menu. No existing view is edited.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class SettingsShellModule : IShellModule
{
	private readonly SettingsHost _host;

	public SettingsShellModule(SettingsHost host)
	{
		_host = host;
	}

	public void Register(IShellSlots slots, IShellCommands commands)
	{
		commands.Register(new ShellCommand
		{
			Id = Constants.Settings.OpenCommandId,
			Title = Constants.Settings.OpenCommandTitle,
			DefaultGesture = Constants.Settings.OpenCommandGesture,
			AllowInTextInput = true,
			ExecuteAsync = _ => _host.OpenAsync(),
		});

		commands.AddMenuEntry(Constants.Menus.Main, Constants.Settings.OpenCommandId, Constants.Settings.OpenButtonOrder);
	}
}
