using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.State;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.ViewModels;
using TessynDesktop.WPF.Views;

namespace TessynDesktop.WPF.Tests;

/// <summary>The layout slots, the command menu and the dialog window as real WPF controls on an STA thread (W0).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class ShellSlotViewTests
{
	private sealed class MemoryState : IClientStateStore
	{
		public ClientStateModel State { get; } = new();

		public Task LoadAsync() => Task.CompletedTask;

		public void Update(Action<ClientStateModel> change) => change(State);

		public Task FlushAsync() => Task.CompletedTask;
	}

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

	private static void Layout(FrameworkElement element)
	{
		element.Measure(new Size(600, 400));
		element.Arrange(new Rect(0, 0, 600, 400));
		element.UpdateLayout();
	}

	[Fact]
	public void SlotHost_ShowsTheContributionsOfItsSlot_InOrder_WithTheHostsDataContext()
	{
		StaTestRunner.Run(() =>
		{
			var slots = new ShellSlots();
			slots.Add(new ShellSlotContribution { SlotId = Constants.Slots.CommandBar, Order = 2, Create = () => new TextBlock { Text = "second" } });
			slots.Add(new ShellSlotContribution { SlotId = Constants.Slots.CommandBar, Order = 1, Create = () => new TextBlock { Text = "first" } });
			slots.Add(new ShellSlotContribution { SlotId = Constants.Slots.TreeFooter, Create = () => new TextBlock { Text = "elsewhere" } });
			var host = new SlotHost { SlotId = Constants.Slots.CommandBar, Slots = slots, DataContext = "the pane" };

			host.Populate();
			Layout(host);

			Assert.Equal(["first", "second"], host.Items.Cast<TextBlock>().Select(t => t.Text));
			Assert.Equal("the pane", ((TextBlock)host.Items[0]!).DataContext);
		});
	}

	[Fact]
	public void SlotHost_WithoutContributions_TakesNoSpace()
	{
		StaTestRunner.Run(() =>
		{
			var host = new SlotHost { SlotId = Constants.Slots.CommandBar, Slots = new ShellSlots() };

			host.Populate();
			Layout(host);

			Assert.Empty(host.Items);
			Assert.Equal(0, host.DesiredSize.Height);
			Assert.Equal(0, host.DesiredSize.Width);
		});
	}

	[Fact]
	public void SlotHost_FillsOnlyOnce()
	{
		StaTestRunner.Run(() =>
		{
			var created = 0;
			var slots = new ShellSlots();
			slots.Add(new ShellSlotContribution { SlotId = Constants.Slots.CommandBar, Create = () => { created++; return new TextBlock(); } });
			var host = new SlotHost { SlotId = Constants.Slots.CommandBar, Slots = slots };

			host.Populate();
			host.Populate();

			Assert.Equal(1, created);
			Assert.Single(host.Items);
		});
	}

	[Fact]
	public void CommandMenu_BuildsItemsSeparatorsAndGestures_AndRunsTheCommandWithTheParameter()
	{
		StaTestRunner.Run(() =>
		{
			var commands = new ShellCommands(new ClientSettingsStore(new MemoryState(), NullLogger<ClientSettingsStore>.Instance), NullLogger<ShellCommands>.Instance);
			object? seen = null;
			commands.Register(new ShellCommand { Id = "a", Title = "Rename", DefaultGesture = "F2", ExecuteAsync = p => { seen = p; return Task.CompletedTask; } });
			commands.Register(new ShellCommand { Id = "b", Title = "Delete", ExecuteAsync = _ => Task.CompletedTask, CanExecute = _ => false });
			commands.AddMenuEntry("m", "a", 1, "edit");
			commands.AddMenuEntry("m", "b", 2, "danger");

			var menu = CommandMenu.Build(commands, "m", "node-7")!;

			Assert.Equal(3, menu.Items.Count);
			var rename = (MenuItem)menu.Items[0]!;
			Assert.Equal("Rename", rename.Header);
			Assert.Equal("F2", rename.InputGestureText);
			Assert.IsType<Separator>(menu.Items[1]);
			Assert.False(((MenuItem)menu.Items[2]!).IsEnabled);

			rename.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
			Assert.Equal("node-7", seen);
		});
	}

	[Fact]
	public void CommandMenu_WithNoEntries_BuildsNoMenu()
	{
		StaTestRunner.Run(() =>
		{
			var commands = new ShellCommands(new ClientSettingsStore(new MemoryState(), NullLogger<ClientSettingsStore>.Instance), NullLogger<ShellCommands>.Instance);

			Assert.Null(CommandMenu.Build(commands, Constants.Menus.TreeNode, null));
		});
	}

	[Fact]
	public void Xaml_HostsEverySlotAndMenuItDeclares_ExactlyOnce()
	{
		var xaml = string.Concat(Directory.EnumerateFiles(FindViews(), "*.xaml").Select(File.ReadAllText));
		var ids = typeof(Constants.Slots).GetFields(BindingFlags.Public | BindingFlags.Static).Select(f => (string)f.GetRawConstantValue()!).ToList();

		Assert.NotEmpty(ids);
		var catalog = File.ReadAllText(Path.Combine(FindViews(), "ShellPanelCatalog.cs"));
		Assert.Contains(nameof(Constants.Slots.LeftPanelTabs), catalog);   // FR.6.11: the left-panel slot is hosted by the docking area's panel catalog (Recent and Tags are panels), no longer by XAML
		foreach (var id in ids.Where(i => i != Constants.Slots.LeftPanelTabs))
			Assert.True(xaml.Split($"SlotId=\"{id}\"").Length == 2, $"The slot '{id}' must be hosted exactly once in the XAML");

		Assert.Contains($"CommandMenu.Menu=\"{Constants.Menus.TreeNode}\"", xaml);
	}

	[Fact]
	public void DialogWindow_BindsEveryKindWithoutBindingErrors()
	{
		var listener = new BindingErrors();
		StaTestRunner.Run(() =>
		{
			PresentationTraceSources.Refresh();
			PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
			PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;
			var dialogs = new[]
			{
				new DialogViewModel(DialogKind.Confirm, "Delete", "Delete it?", "Delete", "Cancel") { IsDestructive = true },
				new DialogViewModel(DialogKind.Input, "Rename", "Name", "OK", "Cancel") { InputText = "x", Validate = _ => "bad" },
				new DialogViewModel(DialogKind.Choice, "Delay", "Send later", string.Empty, "Cancel") { Choices = [new DialogChoice("15m", "15 minutes")] },
			};

			foreach (var dialog in dialogs)
			{
				var window = new DialogWindow(dialog);
				Layout(window);
				if (dialog.IsInput)
				{
					dialog.AcceptCommand.Execute(null);
					Layout(window);
					Assert.True(dialog.HasError);
				}
			}

			var destructive = new DialogWindow(dialogs[0]);
			Assert.True(((Button)destructive.FindName("CancelButton")).IsDefault);
			Assert.False(((Button)destructive.FindName("AcceptButton")).IsDefault);
		});

		Assert.Empty(listener.Messages);
	}

	private static string FindViews()
	{
		for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
		{
			var candidate = Path.Combine(dir.FullName, "TessynDesktop.WPF", "Views");
			if (Directory.Exists(candidate))
				return candidate;
		}

		throw new DirectoryNotFoundException("TessynDesktop.WPF/Views was not found above the test output");
	}
}
