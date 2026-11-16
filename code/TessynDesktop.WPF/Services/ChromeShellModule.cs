using System.Windows;
using TessynDesktop.WPF.ViewModels;
using TessynDesktop.WPF.Views;

namespace TessynDesktop.WPF.Services;

/// <summary>
/// W10a: the status bar's model line and usage meters, and the commands for the day/night toggle, the tree toggle and Send, so key bindings (also the ones imported
/// from the Avalonia client) reach them by name.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class ChromeShellModule : IShellModule
{
	private readonly StatusBarViewModel _statusBar;
	private readonly TitleBarViewModel _titleBar;
	private readonly MainViewModel _main;
	private readonly IAppearanceService _appearance;

	public ChromeShellModule(StatusBarViewModel statusBar, TitleBarViewModel titleBar, MainViewModel main, IAppearanceService appearance)
	{
		_statusBar = statusBar;
		_titleBar = titleBar;
		_main = main;
		_appearance = appearance;
	}

	public void Register(IShellSlots slots, IShellCommands commands)
	{
		slots.Add(new ShellSlotContribution
		{
			SlotId = Constants.Slots.StatusBarItems,
			Order = Constants.StatusBar.ItemOrder,
			Create = () => new StatusBarItems { DataContext = _statusBar },
		});

		commands.Register(new ShellCommand
		{
			Id = Constants.Appearance.ToggleThemeCommandId,
			Title = Constants.Appearance.ToggleThemeCommandTitle,
			AllowInTextInput = true,
			ExecuteAsync = _ =>
			{
				_appearance.ToggleTheme();
				return Task.CompletedTask;
			},
		});
		commands.Register(new ShellCommand
		{
			Id = Constants.Appearance.ToggleTreeCommandId,
			Title = Constants.Appearance.ToggleTreeCommandTitle,
			AllowInTextInput = true,
			ExecuteAsync = _ =>
			{
				_titleBar.SetTreeVisible(!_titleBar.IsTreeVisible);
				return Task.CompletedTask;
			},
		});
		commands.Register(new ShellCommand
		{
			Id = Constants.Appearance.SendCommandId,
			Title = Constants.Appearance.SendCommandTitle,
			DefaultGesture = Constants.Appearance.SendCommandGesture,
			AllowInTextInput = true,
			IgnoreRepeat = true,   // a held key must not send the same prompt twice
			CanExecute = _ => _main.Session?.SendCommand.CanExecute(null) == true,
			ExecuteAsync = _ => _main.Session?.SendCommand.ExecuteAsync(null) ?? Task.CompletedTask,
		});
	}
}
