using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using TessynDesktop.WPF.ViewModels;
using TessynDesktop.WPF.Views;

namespace TessynDesktop.WPF.Services;

/// <summary>
/// W8: the "Send later" header button and the command <c>session.sendLater</c> (Ctrl+Shift+Enter by default, editable in the key bindings;
/// a right-click on Send runs the same command). The command's parameter is the session pane; from a key it is the open one.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class ScheduleShellModule : IShellModule
{
	private readonly MainViewModel _main;

	public ScheduleShellModule(MainViewModel main)
	{
		_main = main;
	}

	public void Register(IShellSlots slots, IShellCommands commands)
	{
		// The schedule indicator is no longer a header contribution: it is an icon in the side column of the input area (SessionPane.xaml).

		// A button as well as the right-click: a keyboard or assistive-technology user cannot right-click, and the key is not discoverable.
		slots.Add(new ShellSlotContribution
		{
			// In the header, not the command bar: an extra button there wrapped the bar onto another row and shortened the transcript.
			SlotId = Constants.Slots.SessionHeaderToolbar,
			Order = Constants.Schedules.SendLaterButtonOrder,
			Create = CreateSendLaterButton,
		});

		commands.Register(new ShellCommand
		{
			Id = Constants.Schedules.SendLaterCommandId,
			Title = Constants.Schedules.SendLaterTitle,
			DefaultGesture = Constants.Schedules.SendLaterGesture,
			AllowInTextInput = true,
			IgnoreRepeat = true,
			CanExecute = parameter => PaneOf(parameter)?.HasSession == true,
			ResolveParameter = () => _main.Session,
			ExecuteAsync = parameter => PaneOf(parameter)?.SendLaterCommand.ExecuteAsync(null) ?? Task.CompletedTask,
		});
	}

	private static Button CreateSendLaterButton()
	{
		var button = new Button
		{
			Content = Constants.Schedules.SendLaterTitle,
			Margin = new Thickness(8, 0, 0, 0),
			Padding = new Thickness(8, 4, 8, 4),
			ToolTip = Constants.Schedules.SendLaterToolTip,
		};
		System.Windows.Automation.AutomationProperties.SetName(button, Constants.Schedules.SendLaterButtonName);
		button.SetBinding(System.Windows.Controls.Primitives.ButtonBase.CommandProperty, new Binding(nameof(SessionPaneViewModel.SendLaterCommand)));
		return button;
	}

	private static SessionPaneViewModel? PaneOf(object? parameter) => parameter as SessionPaneViewModel;
}
