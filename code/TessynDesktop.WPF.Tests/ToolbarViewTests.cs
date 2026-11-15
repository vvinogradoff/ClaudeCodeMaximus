using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Infrastructure;
using TessynDesktop.WPF.Rendering;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.ViewModels;
using TessynDesktop.WPF.Views;

namespace TessynDesktop.WPF.Tests;

/// <summary>The toolbar controls of W4 as real controls bound to a session pane: they bind without errors and drive the view models.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class ToolbarViewTests
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

	private static SessionPaneViewModel Pane(FakeTessynClient client)
	{
		client.Sessions["a"] = FakeSession.WithEntries(2);
		client.Catalog = new CatalogResult([new ModelOptionDto("default", "Default", null), new ModelOptionDto("llama", "Llama", ModelProvider.Ollama)], ["default"], [new ProfileOptionDto("default", "Default")]);
		client.LocalToolsGetHandler = _ => new LocalToolsDto(true, ["Read"], false, [], ["Read", "Bash"], []);
		var pane = new SessionPaneViewModel(client, new FakeTranscriptSurface(), new MarkdigEntryRenderer(new UrlPolicy()), new FakeUrlLauncher(), new ImmediateUiDispatcher(),
			new SwitchLatencyTracker(NullLogger<SwitchLatencyTracker>.Instance), NullLogger<SessionPaneViewModel>.Instance);
		pane.OpenAsync("a").GetAwaiter().GetResult();
		pane.LoadCatalogAsync().GetAwaiter().GetResult();
		pane.SessionOptions.Idle.GetAwaiter().GetResult();
		pane.LocalTools.Idle.GetAwaiter().GetResult();
		return pane;
	}

	private static void Layout(FrameworkElement element)
	{
		element.Measure(new Size(900, 200));
		element.Arrange(new Rect(0, 0, 900, 200));
		element.UpdateLayout();
	}

	private static List<T> FindAll<T>(DependencyObject root) where T : DependencyObject
	{
		var found = new List<T>();
		for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
		{
			var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
			if (child is T match)
				found.Add(match);
			found.AddRange(FindAll<T>(child));
		}

		return found;
	}

	[Fact]
	public void InstructionToolbar_BindsAndItsButtonsDriveTheViewModel()
	{
		var listener = new BindingErrors();
		StaTestRunner.Run(() =>
		{
			PresentationTraceSources.Refresh();
			PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
			PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;
			var client = new FakeTessynClient();
			using var pane = Pane(client);
			var bar = new InstructionToolbar { DataContext = pane };
			Layout(bar);

			var toggles = FindAll<ToggleButton>(bar);
			Assert.Equal(3, toggles.Count);
			Assert.Equal(3, FindAll<Button>(bar).Count);   // Auto-Commit cycles, so it is a button: with Compact now and Terminate
			Assert.All(toggles, t => Assert.True(t.IsEnabled));

			var newBranch = toggles.Single(t => (string)t.GetValue(System.Windows.Automation.AutomationProperties.NameProperty) == "New Branch");
			newBranch.IsChecked = true;   // what a click does: the two-way binding reaches the view model
			Layout(bar);
			Assert.True(pane.Instruction.NewBranch);
			Assert.True(newBranch.IsChecked);
		});

		Assert.Empty(listener.Messages);
	}

	[Fact]
	public void CommandToggles_ShowTheLocalToolsButtonOnlyForALocalModel()
	{
		var listener = new BindingErrors();
		StaTestRunner.Run(() =>
		{
			PresentationTraceSources.Refresh();
			PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
			PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;
			var client = new FakeTessynClient();
			using var pane = Pane(client);
			var toggles = new CommandToggles { DataContext = pane };
			Layout(toggles);

			var tools = FindAll<LocalToolsButton>(toggles).Single();
			var grid = (Grid)tools.Content;
			Assert.Equal(Visibility.Collapsed, grid.Visibility);
			Assert.Equal(2, FindAll<ToggleButton>(toggles).Count(t => t.Content is string s && s is "Streaming" or "Sound"));

			pane.SelectedModel = pane.Models.Single(m => m.Id == "llama");
			Layout(toggles);
			Assert.Equal(Visibility.Visible, grid.Visibility);

			// A short press and release flips the master switch.
			var button = FindAll<ToggleButton>(tools).Single(t => (string?)t.Content == "Tools");
			Assert.True(button.IsChecked);
			pane.LocalTools.ToggleEnabledCommand.Execute(null);
			Layout(toggles);
			Assert.False(button.IsChecked);
			Assert.Equal(0.45, button.Opacity, 2);   // dimmed when off

			// The checklist lists the tools and shows its boxes.
			pane.LocalTools.TogglePopupCommand.Execute(null);
			Layout(toggles);
			Assert.Single(FindAll<Popup>(tools));   // the popup itself cannot open without a window
			Assert.True(pane.LocalTools.IsPopupOpen);
		});

		Assert.Empty(listener.Messages);
	}

	[Fact]
	public void Module_CreatesBothViews()
	{
		StaTestRunner.Run(() =>
		{
			var slots = new ShellSlots();
			new ToolbarShellModule().Register(slots, null!);

			Assert.IsType<InstructionToolbar>(Assert.Single(slots.For(Constants.Slots.InstructionToolbar)).Create());
			Assert.IsType<CommandToggles>(Assert.Single(slots.For(Constants.Slots.CommandBar)).Create());
		});
	}
}
