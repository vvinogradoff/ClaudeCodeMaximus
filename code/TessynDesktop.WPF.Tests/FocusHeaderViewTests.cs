using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Infrastructure;
using TessynDesktop.WPF.Rendering;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.ViewModels;
using TessynDesktop.WPF.Views;

namespace TessynDesktop.WPF.Tests;

/// <summary>
/// FR.6.23 / UC.DS.13 / TC.DS.27 to TC.DS.35, TC.DS.38 (the part a view test can check), TC.DS.39: the header of the Focus panel with real WPF layout: Delete, name, Running / Idle and star on
/// the session pane, Send answers (n) and Session at the right end, the Session view header's sizes, one rename per header, delete and star through the session's one command.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class FocusHeaderViewTests
{
	private readonly FakeTessynClient _client = new();
	private readonly FakeDialogPresenter _presenter = new();
	private readonly FakeShellLayout _layout = new();

	private SessionPaneViewModel CreatePane() => new(
		_client, new FakeTranscriptSurface(), new MarkdigEntryRenderer(new UrlPolicy()), new FakeUrlLauncher(), new ImmediateUiDispatcher(),
		new SwitchLatencyTracker(NullLogger<SwitchLatencyTracker>.Instance), NullLogger<SessionPaneViewModel>.Instance, Constants.Limits.MaxCachedTranscripts,
		new FocusViewModel(_client, new ImmediateUiDispatcher(), NullLogger<FocusViewModel>.Instance), null, new MediaPreviewPolicy()) { ShellLayout = _layout };

	private async Task<SessionPaneViewModel> OpenPaneAsync(string name = "One")
	{
		_client.Sessions["s1"] = FakeSession.WithEntries(2, name);
		var pane = CreatePane();
		await pane.OpenAsync("s1", name);
		return pane;
	}

	private static FocusPanel Panel(SessionPaneViewModel pane, double width = 700)
	{
		var panel = new FocusPanel { DataContext = pane.Focus, Session = pane };
		Layout(panel, width);
		return panel;
	}

	private static void Layout(FrameworkElement element, double width = 700)
	{
		element.Measure(new Size(width, 600));
		element.Arrange(new Rect(0, 0, width, 600));
		element.UpdateLayout();
	}

	private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
	{
		for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
		{
			var child = VisualTreeHelper.GetChild(root, i);
			if (child is T match)
				yield return match;

			foreach (var nested in Descendants<T>(child))
				yield return nested;
		}
	}

	private static Button ButtonNamed(DependencyObject root, string name) =>
		Descendants<Button>(root).Single(b => AutomationProperties.GetName(b) == name);

	private static Button? FindButton(DependencyObject root, string name) =>
		Descendants<Button>(root).FirstOrDefault(b => AutomationProperties.GetName(b) == name);

	private static double Left(FrameworkElement element, FrameworkElement relativeTo) => element.TranslatePoint(default, relativeTo).X;

	private static (TextBox Box, TextBlock Text) TitleParts(SessionIdentity identity) =>
		((TextBox)identity.TitleView.FindName("RenameBox")!, (TextBlock)identity.TitleView.FindName("TitleText")!);

	private static Button DeleteOf(SessionIdentity identity) => (Button)identity.TitleView.FindName("DeleteButton")!;

	[Fact]
	public async Task TheHeader_HoldsDeleteNameStateStarSendAnswersAndSession_AndNothingElse()
	{
		var pane = await OpenPaneAsync("My session");
		StaTestRunner.Run(() =>
		{
			var panel = Panel(pane);
			var header = (DockPanel)panel.FindName("HeaderRow")!;
			var identity = panel.HeaderIdentityView;
			var (_, name) = TitleParts(identity);

			Assert.Equal("My session", name.Text);
			Assert.Equal(20, name.FontSize);
			Assert.Equal(Visibility.Visible, DeleteOf(identity).Visibility);
			var stateText = Descendants<TextBlock>(identity).Single(t => AutomationProperties.GetName(t) == "Session state");
			Assert.Equal(pane.StateText, stateText.Text);
			var star = ButtonNamed(header, "Favourite");
			var send = ButtonNamed(header, "Send answers");
			var session = ButtonNamed(header, "Session");

			// from the left: identity, star, Send answers (n), Session at the right end
			Assert.True(Left(star, header) > Left(name, header));
			Assert.True(Left(send, header) > Left(star, header));
			Assert.True(Left(session, header) > Left(send, header));
			Assert.Equal(header.ActualWidth, Left(session, header) + session.ActualWidth, 1);

			foreach (var absent in new[] { "Back", "Focus", "Send later", "Send", "Find", "Markdown", "JSONL" })
				Assert.Null(FindButton(header, absent));
		});
	}

	[Fact]
	public void WithoutASession_OnlySendAnswersAndSessionAreShown()
	{
		var pane = CreatePane();
		StaTestRunner.Run(() =>
		{
			var panel = Panel(pane);
			var header = (DockPanel)panel.FindName("HeaderRow")!;
			Assert.Equal(Visibility.Collapsed, DeleteOf(panel.HeaderIdentityView).Visibility);
			Assert.Equal(Visibility.Collapsed, ButtonNamed(header, "Favourite").Visibility);
			Assert.Equal(string.Empty, TitleParts(panel.HeaderIdentityView).Text.Text);
			Assert.Equal(Visibility.Visible, ButtonNamed(header, "Send answers").Visibility);
			Assert.False(ButtonNamed(header, "Send answers").Command.CanExecute(null));
			Assert.Equal(Visibility.Visible, ButtonNamed(header, "Session").Visibility);
			Assert.True(ButtonNamed(header, "Session").ActualWidth > 0);
		});
	}

	[Fact]
	public async Task TheHeaderRow_HasTheSessionViewHeadersMarginsHeightAndSizes()
	{
		var pane = await OpenPaneAsync("Same session");
		StaTestRunner.Run(() =>
		{
			var panel = Panel(pane);
			var focusRow = (DockPanel)panel.FindName("HeaderRow")!;
			var view = new SessionPane { DataContext = pane };
			Layout(view);
			var sessionIdentity = Descendants<SessionIdentity>(view).Single();
			var sessionRow = (DockPanel)VisualTreeHelper.GetParent(sessionIdentity);
			var sessionHeader = (StackPanel)VisualTreeHelper.GetParent(sessionRow);

			Assert.Equal(new Thickness(16, 10, 16, 8), focusRow.Margin);
			Assert.Equal(sessionHeader.Margin, focusRow.Margin);
			Assert.Equal(sessionRow.ActualHeight, focusRow.ActualHeight, 0.5);

			var (_, focusName) = TitleParts(panel.HeaderIdentityView);
			var (_, sessionName) = TitleParts(sessionIdentity);
			Assert.Equal(sessionName.FontSize, focusName.FontSize);
			Assert.Equal(sessionName.FontWeight, focusName.FontWeight);
			var focusDelete = DeleteOf(panel.HeaderIdentityView);
			var sessionDelete = DeleteOf(sessionIdentity);
			Assert.Equal(sessionDelete.ActualWidth, focusDelete.ActualWidth, 0.5);
			Assert.Equal(sessionDelete.ActualHeight, focusDelete.ActualHeight, 0.5);
			Assert.Equal(sessionDelete.Margin, focusDelete.Margin);
			Assert.Equal(sessionDelete.Padding, focusDelete.Padding);

			// the Session button is sized and styled like the Focus button of the Session view
			var focusButton = (Button)view.FindName("FocusButton")!;
			var sessionButton = (Button)panel.FindName("SessionButton")!;
			Assert.Equal(focusButton.Padding, sessionButton.Padding);
			Assert.Equal(focusButton.Margin, sessionButton.Margin);
			Assert.False(sessionButton.Focusable);
			Assert.Equal(focusButton.ActualHeight, sessionButton.ActualHeight, 0.5);
		});
	}

	[Fact]
	public async Task TheHeader_HasAnEightPixelPaddingBelow_AboveTheFirstCardOrTheErrorLine()
	{
		var pane = await OpenPaneAsync();
		StaTestRunner.Run(() =>
		{
			var panel = Panel(pane);
			var header = (DockPanel)panel.FindName("HeaderRow")!;
			Assert.Equal(8, header.Margin.Bottom);

			var grid = (Grid)panel.Content;
			var scroll = grid.Children.OfType<ScrollViewer>().Single();
			var content = (FrameworkElement)scroll.Content;
			var error = grid.Children.OfType<TextBlock>().Single(t => Grid.GetRow(t) == 1);
			Assert.Equal(0, error.Margin.Top);   // the header's 8 px are the only space above the error line
			Assert.Equal(0, content.Margin.Top);   // ... and above the first card
			Assert.Equal(header.ActualHeight + header.Margin.Top + header.Margin.Bottom, scroll.TranslatePoint(default, panel).Y, 0.5);
		});
	}

	[Fact]
	public async Task RenamingInFocus_EnterRenames_AndNoBoxOpensInTheSessionViewHeader()
	{
		var pane = await OpenPaneAsync("Old name");
		StaTestRunner.Run(() =>
		{
			var panel = Panel(pane);
			var view = new SessionPane { DataContext = pane };
			Layout(view);
			var focusIdentity = panel.HeaderIdentityView;
			var sessionIdentity = Descendants<SessionIdentity>(view).Single();
			var (focusBox, focusText) = TitleParts(focusIdentity);
			var (sessionBox, sessionText) = TitleParts(sessionIdentity);

			focusIdentity.TitleView.BeginEdit(pane);
			Layout(panel);
			Layout(view);
			Assert.Equal(Visibility.Visible, focusBox.Visibility);
			Assert.Equal(Visibility.Collapsed, focusText.Visibility);
			Assert.Equal("Old name", focusBox.Text);
			Assert.Equal(Visibility.Collapsed, sessionBox.Visibility);   // one rename, one box
			Assert.Equal(Visibility.Visible, sessionText.Visibility);

			focusBox.Text = "New name";
			focusIdentity.TitleView.CommitEdit();
			Layout(panel);
			Assert.Equal(Visibility.Collapsed, focusBox.Visibility);
		});

		await Task.Delay(50);
		Assert.Equal("New name", pane.Title);
		Assert.Contains("tree.renameNode:s1:New name", _client.Calls);
		StaTestRunner.Run(() => Assert.Equal("New name", TitleParts(Panel(pane).HeaderIdentityView).Text.Text));
	}

	[Fact]
	public async Task RenamingInFocus_EscAnEmptyAndAnUnchangedNameChangeNothing_AndARefusalShowsAsANotice()
	{
		var pane = await OpenPaneAsync("Old name");
		StaTestRunner.Run(() =>
		{
			var panel = Panel(pane);
			var title = panel.HeaderIdentityView.TitleView;
			var (box, _) = TitleParts(panel.HeaderIdentityView);

			title.BeginEdit(pane);
			box.Text = "Typed but cancelled";
			title.CancelEdit();   // Esc, or leaving the box
			Assert.False(title.IsEditing);

			title.BeginEdit(pane);
			box.Text = "   ";
			title.CommitEdit();

			title.BeginEdit(pane);
			title.CommitEdit();   // unchanged
		});

		await Task.Delay(50);
		Assert.Equal("Old name", pane.Title);
		Assert.DoesNotContain(_client.Calls, c => c.StartsWith("tree.renameNode", StringComparison.Ordinal));

		_client.RenameNodeHandler = (_, _) => throw new InvalidOperationException("Name taken");
		StaTestRunner.Run(() =>
		{
			var panel = Panel(pane);
			var title = panel.HeaderIdentityView.TitleView;
			title.BeginEdit(pane);
			((TextBox)title.FindName("RenameBox")!).Text = "Taken";
			title.CommitEdit();
		});

		await Task.Delay(50);
		Assert.Equal("Old name", pane.Title);
		Assert.Contains("Name taken", pane.Notice);
	}

	[Fact]
	public async Task TheStarInFocus_TogglesTheSameFlagAsTheSessionViewsStar()
	{
		var pane = await OpenPaneAsync();
		StaTestRunner.Run(() =>
		{
			var panel = Panel(pane);
			var star = ButtonNamed((DockPanel)panel.FindName("HeaderRow")!, "Favourite");
			Assert.True(star.IsEnabled);
			Assert.False(pane.IsFavorite);

			star.Command.Execute(null);
			Assert.True(pane.IsFavorite);   // the one flag; the Session view's star (the slot's FavoriteButton) is bound to the same property
			star.Command.Execute(null);
			Assert.False(pane.IsFavorite);
		});
	}

	[Fact]
	public async Task DeleteInFocus_AsksFirst_CancelKeepsTheSession_AndConfirmingDeletesIt()
	{
		var builder = new TreeBuilder();
		_client.Sessions["s1"] = FakeSession.WithEntries(2, "One");
		_client.Directories = [builder.Directory("d1", "Repo", [], builder.Session("s1", "One"))];
		var ui = new ImmediateUiDispatcher();
		var operations = new TreeOperations(_client, new DialogService(_presenter, ui), new ScriptedFolderPicker([], NullLogger<ScriptedFolderPicker>.Instance), NullLogger<TreeOperations>.Instance);
		var tree = new TreeViewModel(_client, ui, NullLogger<TreeViewModel>.Instance, TimeSpan.Zero, operations);
		await tree.LoadAsync();
		var commands = new ShellCommands(SettingsTestSupport.NewStore(), NullLogger<ShellCommands>.Instance);
		var context = new TreeContext { Current = tree };
		new TreeShellModule(context).Register(new ShellSlots(), commands);
		var pane = await OpenPaneAsync();
		_client.DeleteNodeHandler = (_, force) => new TreeDeleteResult(force, force ? null : "sessionFile");

		void ClickDelete() => StaTestRunner.Run(() =>
		{
			var panel = Panel(pane);
			var title = panel.HeaderIdentityView.TitleView;
			title.Commands = commands;
			title.Tree = context;
			DeleteOf(panel.HeaderIdentityView).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
		});

		_presenter.Answer = d =>
		{
			d.CancelCommand.Execute(null);
			return Task.CompletedTask;
		};
		ClickDelete();
		await Task.Delay(100);
		Assert.Single(_presenter.Shown);
		Assert.True(_presenter.Shown[0].IsConfirm);
		Assert.Equal(["tree.deleteNode:s1:False"], _client.Calls.Where(c => c.StartsWith("tree.deleteNode", StringComparison.Ordinal)));
		Assert.NotNull(tree.FindNode("s1"));

		_presenter.Answer = d =>
		{
			d.AcceptCommand.Execute(null);
			return Task.CompletedTask;
		};
		ClickDelete();
		await Task.Delay(100);
		Assert.Equal(["tree.deleteNode:s1:False", "tree.deleteNode:s1:False", "tree.deleteNode:s1:True"], _client.Calls.Where(c => c.StartsWith("tree.deleteNode", StringComparison.Ordinal)));
	}

	[Fact]
	public async Task SendAnswers_KeepsItsCommandAndEnabling_InTheHeader()
	{
		var pane = await OpenPaneAsync();
		StaTestRunner.Run(() =>
		{
			var panel = Panel(pane);
			var send = (Button)panel.FindName("SendAnswersButton")!;
			Assert.Same(pane.Focus.SendAnswersCommand, send.Command);
			Assert.Equal(pane.Focus.SendAnswersCommand.CanExecute(null), send.IsEnabled);
			Assert.Equal("Send answers", AutomationProperties.GetName(send));
		});
	}

	[Fact]
	public async Task ANarrowPanel_TrimsTheNameAndKeepsTheButtonsInPlace()
	{
		var pane = await OpenPaneAsync(new string('W', 120));
		StaTestRunner.Run(() =>
		{
			var panel = Panel(pane, 360);
			var header = (DockPanel)panel.FindName("HeaderRow")!;
			var (_, name) = TitleParts(panel.HeaderIdentityView);
			Assert.Equal(TextTrimming.CharacterEllipsis, name.TextTrimming);
			name.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
			Assert.True(name.ActualWidth < name.DesiredSize.Width, "the name is trimmed");
			foreach (var button in new[] { "Favourite", "Send answers", "Session" })
			{
				var b = ButtonNamed(header, button);
				Assert.True(b.ActualWidth > 0);
				Assert.True(Left(b, header) + b.ActualWidth <= header.ActualWidth + 0.5, button + " stays inside the row");
			}

			var session = ButtonNamed(header, "Session");
			Assert.Equal(header.ActualWidth, Left(session, header) + session.ActualWidth, 1);
			Assert.True(Left(name, header) + name.ActualWidth <= Left(ButtonNamed(header, "Favourite"), header) + 0.5, "the name does not run under the buttons");
		});
	}

	[Fact]
	public async Task TheSessionButton_BringsTheSessionViewForward_WithoutTheKeyboardFocus_AndHasTheTooltipAndName()
	{
		var pane = await OpenPaneAsync();
		StaTestRunner.Run(() =>
		{
			var panel = Panel(pane);
			var button = (Button)panel.FindName("SessionButton")!;
			Assert.Equal("Session", AutomationProperties.GetName(button));
			Assert.Equal(Constants.ShellLayout.SessionButtonToolTip, button.ToolTip);
			Assert.False(button.Focusable);

			button.Command.Execute(null);
			Assert.Equal([Constants.DockedShell.SessionPanelId], _layout.BroughtForward);
		});
	}
}
