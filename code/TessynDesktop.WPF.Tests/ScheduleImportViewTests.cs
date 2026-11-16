using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Rendering;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.State;
using TessynDesktop.WPF.Infrastructure;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.ViewModels;
using TessynDesktop.WPF.Views;

namespace TessynDesktop.WPF.Tests;

/// <summary>W8 and W7 as real WPF controls on an STA thread: the schedule indicator, the right-click on Send, the tree row clock, the import picker window and the modules' registrations.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class ScheduleImportViewTests
{
	private sealed class BindingErrors : TraceListener
	{
		public List<string> Messages { get; } = [];

		public override void Write(string? message) => Capture(message);

		public override void WriteLine(string? message) => Capture(message);

		private void Capture(string? message)
		{
			if (message != null && message.Contains("BindingExpression", StringComparison.Ordinal))
				lock (Messages)
					Messages.Add(message);
		}
	}

	private sealed class MemoryState : IClientStateStore
	{
		public ClientStateModel State { get; } = new();

		public Task LoadAsync() => Task.CompletedTask;

		public void Update(Action<ClientStateModel> change) => change(State);

		public Task FlushAsync() => Task.CompletedTask;
	}

	private static readonly DateTimeOffset Now = new(2026, 11, 16, 10, 0, 0, TimeSpan.Zero);

	private static void Layout(FrameworkElement element)
	{
		element.Measure(new Size(900, 700));
		element.Arrange(new Rect(0, 0, 900, 700));
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

	/// <summary>Visible and every ancestor visible: an element outside a window has no IsVisible, so the chain is walked by hand.</summary>
	private static bool Shown(UIElement element)
	{
		for (DependencyObject? current = element; current != null; current = VisualTreeHelper.GetParent(current))
		{
			if (current is UIElement { Visibility: not Visibility.Visible })
				return false;
		}

		return true;
	}

	private static void WithBindingCheck(Action body)
	{
		var listener = new BindingErrors();
		StaTestRunner.Run(() =>
		{
			PresentationTraceSources.Refresh();
			PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
			PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;
			body();
		});
		Assert.Empty(listener.Messages);
	}

	private static FakeTessynClient ClientWithSchedules(params ScheduleDto[] schedules)
	{
		var client = new FakeTessynClient();
		client.ServerInfo = new HelloResult(1, "test", "machine", "run", DateTimeOffset.UtcNow, [RemoteProtocol.Schedules.Capability], 1, 0);
		client.Sessions["a"] = FakeSession.WithEntries(2);
		client.SchedulesListHandler = _ => new SchedulesListResult(schedules);
		return client;
	}

	private static ScheduleDto Schedule(string id, bool mailbox = false) =>
		new(id, "a", ScheduleKindDto.At, "wake", mailbox ? string.Empty : "Delayed send", Now.AddHours(1), null, MissedFireDto.FireOnce, 0, 0, mailbox);

	private static SessionPaneViewModel Pane(FakeTessynClient client, FakeDialogPresenter? presenter = null) =>
		new(client, new FakeTranscriptSurface(), new MarkdigEntryRenderer(new UrlPolicy()), new FakeUrlLauncher(), new ImmediateUiDispatcher(),
			new SwitchLatencyTracker(NullLogger<SwitchLatencyTracker>.Instance), NullLogger<SessionPaneViewModel>.Instance)
		{
			Dialogs = new DialogService(presenter ?? new FakeDialogPresenter(), new ImmediateUiDispatcher()),
			ScheduleClock = new FixedClock(Now),
			ScheduleZone = TimeZoneInfo.Utc,
		};

	// ---- W8 ----

	[Fact]
	public void TheIndicator_IsHiddenWithoutSchedules_AndShowsTheCountWithThem()
	{
		WithBindingCheck(() =>
		{
			var none = Pane(ClientWithSchedules());
			none.OpenAsync("a", "A").GetAwaiter().GetResult();
			var hidden = new ScheduleIndicator { DataContext = none };
			Layout(hidden);
			Assert.Empty(Descendants<ToggleButton>(hidden).Where(Shown));

			var two = Pane(ClientWithSchedules(Schedule("x"), Schedule("m", mailbox: true)));
			two.OpenAsync("a", "A").GetAwaiter().GetResult();
			var shown = new ScheduleIndicator { DataContext = two };
			Layout(shown);

			var button = Descendants<ToggleButton>(shown).Single(b => AutomationProperties.GetName(b) == Constants.Schedules.IndicatorName);
			Assert.True(Shown(button));
			Assert.Contains(Descendants<TextBlock>(button), t => t.Text == "2");

			button.IsChecked = true;   // opens the list
			Assert.True(two.Schedules.IsOpen);
		});
	}

	[Fact]
	public void AScheduleRow_OffersCancel_ForAUserScheduleOnly_AndRunsTheListsCommand()
	{
		WithBindingCheck(() =>
		{
			var client = ClientWithSchedules(Schedule("x"), Schedule("m", mailbox: true));
			var pane = Pane(client);
			pane.OpenAsync("a", "A").GetAwaiter().GetResult();
			var indicator = new ScheduleIndicator { DataContext = pane };
			var template = (DataTemplate)indicator.Resources["ScheduleRow"];

			FrameworkElement Row(ScheduleItemViewModel item)
			{
				var row = (FrameworkElement)template.LoadContent();
				row.DataContext = item;
				Layout(row);
				return row;
			}

			var own = Row(pane.Schedules.Own[0]);
			var mailbox = Row(pane.Schedules.Mailbox[0]);

			var cancel = Descendants<Button>(own).Single();
			Assert.True(Shown(cancel));
			Assert.Equal(Constants.Schedules.CancelPrefix + "x", AutomationProperties.GetName(cancel));
			Assert.False(Shown(Descendants<Button>(mailbox).Single()));   // read-only

			cancel.Command.Execute(cancel.CommandParameter);
			Assert.Contains("schedules.remove:x", client.Calls);
		});
	}

	[Fact]
	public void ARightClickOnSend_OpensTheSendLaterDialog_OnlyWhenItCanRun()
	{
		WithBindingCheck(() =>
		{
			var presenter = new FakeDialogPresenter();
			presenter.Answer = dialog =>
			{
				dialog.CancelCommand.Execute(null);
				return Task.CompletedTask;
			};
			var idle = Pane(ClientWithSchedules(), presenter);
			var idleView = new SessionPane { DataContext = idle };
			Layout(idleView);
			var idleSend = (Button)idleView.FindName("SendButton")!;
			idleSend.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Right) { RoutedEvent = UIElement.PreviewMouseRightButtonUpEvent, Source = idleSend });
			Assert.Empty(presenter.Shown);   // no session open: nothing to schedule

			var pane = Pane(ClientWithSchedules(), presenter);
			pane.OpenAsync("a", "A").GetAwaiter().GetResult();   // before the view exists: the open resumes on another thread
			pane.InputText = "later please";
			var view = new SessionPane { DataContext = pane };
			Layout(view);
			var send = (Button)view.FindName("SendButton")!;
			send.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Right) { RoutedEvent = UIElement.PreviewMouseRightButtonUpEvent, Source = send });

			var dialog = Assert.Single(presenter.Shown);
			Assert.Equal(Constants.Schedules.DialogTitle, dialog.Title);
			Assert.Equal(Constants.Schedules.DefaultAnswer, dialog.InputText);
			Assert.Equal("later please", pane.InputText);   // cancelled: the text stays
		});
	}

	[Fact]
	public void ATreeRow_ShowsTheClockOnlyForASessionWithSchedules()
	{
		WithBindingCheck(() =>
		{
			var client = new FakeTessynClient();
			var builder = new TreeBuilder();
			client.Directories = [builder.Directory("d1", "Repo", [],
				builder.SessionWith(new SessionStateDto("s1", false, false, null, false, null, null, false, null, HasSchedules: true), "Scheduled"),
				builder.SessionWith(new SessionStateDto("s2", false, false, null, false, null, null, false, null), "Plain"))];
			var tree = new TreeViewModel(client, new ImmediateUiDispatcher(), NullLogger<TreeViewModel>.Instance, TimeSpan.Zero);
			tree.LoadAsync().GetAwaiter().GetResult();
			tree.ToggleExpandCommand.Execute(tree.Rows[0]);   // the directory's sessions show when it is open
			var panel = new TreePanel { DataContext = tree };
			Layout(panel);
			Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Loaded);
			Layout(panel);

			Assert.NotEmpty(Descendants<TextBlock>(panel).Where(t => t.Text == "Scheduled"));
			var clocks = Descendants<TextBlock>(panel).Where(t => AutomationProperties.GetName(t) == "Schedule indicator" && Shown(t)).ToList();

			Assert.Single(clocks);
		});
	}

	[Fact]
	public void TheSendLaterCommand_RunsFromTheRegistry_WithThePaneAsItsParameter()
	{
		StaTestRunner.Run(() =>
		{
			var presenter = new FakeDialogPresenter();
			presenter.Answer = dialog =>
			{
				dialog.InputText = "45m";
				dialog.AcceptCommand.Execute(null);
				return Task.CompletedTask;
			};
			var client = ClientWithSchedules();
			var pane = Pane(client, presenter);
			pane.OpenAsync("a", "A").GetAwaiter().GetResult();
			pane.InputText = "ping";
			var (commands, slots) = Registered(new ScheduleShellModule(null!));

			var command = commands.Find(Constants.Schedules.SendLaterCommandId)!;
			Assert.Equal("Ctrl+Shift+Enter", commands.GetGesture(Constants.Schedules.SendLaterCommandId));
			Assert.True(command.AllowInTextInput);
			Assert.False(command.CanExecute!(null));
			Assert.True(command.CanExecute!(pane));
			// The indicator (20) and the "Send later" button (30) are both in the session header; the command bar got no extra button (it wrapped and shortened the transcript).
			Assert.Equal([Constants.Schedules.IndicatorOrder, Constants.Schedules.SendLaterButtonOrder], slots.For(Constants.Slots.SessionHeaderToolbar).Select(s => s.Order));
			Assert.Empty(slots.For(Constants.Slots.CommandBar));

			Assert.True(commands.ExecuteAsync(Constants.Schedules.SendLaterCommandId, pane).GetAwaiter().GetResult());

			Assert.Contains("schedules.add:a:Delay", client.Calls);
			Assert.Equal(string.Empty, pane.InputText);
		});
	}

	// ---- W7 ----

	[Fact]
	public void TheImportModule_RegistersTheCommands_TheTreeMenuEntryAndTheTitleBarButton_WithoutKeyConflicts()
	{
		StaTestRunner.Run(() =>
		{
			var (commands, slots) = Registered(new ImportShellModule(null!, new TreeContext()), new ScheduleShellModule(null!));

			Assert.Equal("Ctrl+I", commands.GetGesture(Constants.Import.OpenCommandId));
			Assert.Empty(commands.GetConflicts());
			Assert.Contains(commands.BuildMenu(Constants.Menus.TreeNode, new TreeNodeViewModel("d", TreeNodeKind.Directory, "D", null)), m => m.Title == Constants.Import.TreeCommandTitle);
			Assert.DoesNotContain(commands.BuildMenu(Constants.Menus.TreeNode, new TreeNodeViewModel("s", TreeNodeKind.Session, "S", null)).Where(m => m.Title == Constants.Import.TreeCommandTitle), m => m.IsEnabled);
			var button = Assert.IsType<Button>(Assert.Single(slots.For(Constants.Slots.TitleBarToolbar)).Create());
			Assert.Equal(Constants.Import.OpenButtonAutomationName, AutomationProperties.GetName(button));
		});
	}

	[Fact]
	public void TheImportWindow_ShowsTheSessions_GreysTheOnesThatCannotBeImported_AndBindsWithoutErrors()
	{
		WithBindingCheck(() =>
		{
			var client = new FakeTessynClient();
			client.ImportListHandler = (_, _, _, _, _) => new ImportListResult(
			[
				new ImportCandidateDto("ok", "Fix the build", "p", Now, 4, false, false, "proj"),
				new ImportCandidateDto("empty", null, "", Now, 0, true, false, "proj"),
				new ImportCandidateDto("done", "Imported already", "p", Now, 2, false, true, "proj"),
			], 3);
			var picker = new ImportPickerViewModel(client, NullLogger.Instance, TimeSpan.Zero, 1);
			picker.InitializeAsync([new ImportTargetViewModel("d1", "Repo", 0, true, "d1")], "d1").GetAwaiter().GetResult();
			var window = new ImportPickerWindow(picker);
			var root = (FrameworkElement)window.Content;
			Layout(root);

			var boxes = Descendants<CheckBox>(root).Where(c => AutomationProperties.GetName(c).StartsWith(Constants.Import.SelectPrefix, StringComparison.Ordinal)).ToDictionary(c => AutomationProperties.GetName(c));
			Assert.True(boxes[Constants.Import.SelectPrefix + "ok"].IsEnabled);
			Assert.False(boxes[Constants.Import.SelectPrefix + "empty"].IsEnabled);
			Assert.False(boxes[Constants.Import.SelectPrefix + "done"].IsEnabled);

			var import = Descendants<Button>(root).Single(b => AutomationProperties.GetName(b) == Constants.Import.ImportButtonName);
			Assert.False(import.Command.CanExecute(null));
			boxes[Constants.Import.SelectPrefix + "ok"].IsChecked = true;
			Assert.True(import.Command.CanExecute(null));   // ticking is enough: the view binds the selection to the picker

			var search = Descendants<TextBox>(root).Single(t => AutomationProperties.GetName(t) == Constants.Import.SearchBoxName);
			Assert.Contains(search.InputBindings.OfType<KeyBinding>(), k => k.Key == Key.Return && ReferenceEquals(k.Command, picker.SearchCommand));
			Assert.Contains(Descendants<CheckBox>(root), c => AutomationProperties.GetName(c) == Constants.Import.GlobalName);
			Assert.Contains(Descendants<ComboBox>(root), c => AutomationProperties.GetName(c) == Constants.Import.TargetName);
		});
	}

	private static (ShellCommands Commands, ShellSlots Slots) Registered(params IShellModule[] modules)
	{
		var slots = new ShellSlots();
		var commands = new ShellCommands(new ClientSettingsStore(new MemoryState(), NullLogger<ClientSettingsStore>.Instance), NullLogger<ShellCommands>.Instance);
		foreach (var module in modules)
			module.Register(slots, commands);

		return (commands, slots);
	}

	private sealed class FixedClock(DateTimeOffset now) : TimeProvider
	{
		public override DateTimeOffset GetUtcNow() => now;
	}
}
