using System.Windows;
using System.Windows.Controls;

namespace TessynDesktop.WPF.Services;

/// <summary>W6: the command <c>app.openSettings</c> (Ctrl+, by default, editable in the Keyboard shortcuts tab) and a Settings button in the title-bar toolbar. No existing view is edited.</summary>
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

		slots.Add(new ShellSlotContribution
		{
			SlotId = Constants.Slots.TitleBarToolbar,
			Order = Constants.Settings.OpenButtonOrder,
			Create = () => CreateButton(commands),
		});
	}

	private static Button CreateButton(IShellCommands commands)
	{
		var button = new Button
		{
			Content = Constants.Settings.OpenButtonText,
			Margin = new Thickness(8, 4, 8, 4),
			Padding = new Thickness(10, 2, 10, 2),
			ToolTip = Constants.Settings.OpenCommandTitle,
		};
		System.Windows.Automation.AutomationProperties.SetName(button, Constants.Settings.OpenButtonText);
		button.Click += (_, _) => _ = commands.ExecuteAsync(Constants.Settings.OpenCommandId, null);
		return button;
	}
}
