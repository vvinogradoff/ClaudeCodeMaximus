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
			var bar = new InstructionToolbar { DataContext = new { Session = pane } };   // the title-bar slot's DataContext is the MainViewModel
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
			Assert.Equal(2, FindAll<ToggleButton>(toggles).Count(t => System.Windows.Automation.AutomationProperties.GetName(t) is "Streaming Session Mode" or "Completion sound"));

			pane.SelectedModel = pane.Models.Single(m => m.Id == "llama");
			Layout(toggles);
			Assert.Equal(Visibility.Visible, grid.Visibility);

			// A short press and release flips the master switch.
			var button = FindAll<Button>(tools).Single(t => (string?)t.Content == "Tools");
			Assert.Equal(FontWeights.SemiBold, button.FontWeight);   // on
			pane.LocalTools.ToggleEnabledCommand.Execute(null);
			Layout(toggles);
			Assert.Equal(FontWeights.Normal, button.FontWeight);
			Assert.Equal(0.45, button.Opacity, 2);   // dimmed when off

			// The checklist lists the tools and shows its boxes.
			pane.LocalTools.TogglePopupCommand.Execute(null);
			Layout(toggles);
			Assert.Single(FindAll<Popup>(tools));   // the popup itself cannot open without a window
			Assert.True(pane.LocalTools.IsPopupOpen);
		});

		Assert.Empty(listener.Messages);
	}

	/// <summary>Space, Enter and a UI Automation Invoke on the Tools button all run the command and store the profile (a native toggle would only flip its look).</summary>
	[Fact]
	public void ToolsButton_SpaceEnterAndInvoke_StoreTheMasterSwitch()
	{
		StaTestRunner.Run(() =>
		{
			var client = new FakeTessynClient();
			using var pane = Pane(client);
			pane.SelectedModel = pane.Models.Single(m => m.Id == "llama");
			var toggles = new CommandToggles { DataContext = pane };
			using var source = new System.Windows.Interop.HwndSource(new System.Windows.Interop.HwndSourceParameters("tools-test") { ParentWindow = new IntPtr(-3), Width = 900, Height = 200 })
			{
				RootVisual = toggles,
			};
			Layout(toggles);
			var button = FindAll<Button>(toggles).Single(t => (string?)t.Content == "Tools");
			int Stores() => client.Calls.Count(c => c.StartsWith("localTools.set:", StringComparison.Ordinal));
			Assert.Equal(0, Stores());

			RaiseKey(button, source, System.Windows.Input.Key.Space, down: true);
			RaiseKey(button, source, System.Windows.Input.Key.Space, down: false);
			Assert.Equal(1, Stores());

			RaiseKey(button, source, System.Windows.Input.Key.Enter, down: true);
			Assert.Equal(2, Stores());

			var invoke = (System.Windows.Automation.Provider.IInvokeProvider)System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(button)
				.GetPattern(System.Windows.Automation.Peers.PatternInterface.Invoke)!;
			invoke.Invoke();
			for (var i = 0; i < 200 && Stores() < 3; i++)   // Invoke is posted to the dispatcher: pump it until the command has run
			{
				button.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
				Thread.Sleep(10);
			}
			Assert.Equal(3, Stores());
		});
	}

	private static void RaiseKey(UIElement target, PresentationSource source, System.Windows.Input.Key key, bool down)
	{
		target.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, source, 0, key)
		{
			RoutedEvent = down ? System.Windows.Input.Keyboard.KeyDownEvent : System.Windows.Input.Keyboard.KeyUpEvent,
		});
	}

	[Fact]
	public void Module_CreatesBothViews()
	{
		StaTestRunner.Run(() =>
		{
			var slots = new ShellSlots();
			new ToolbarShellModule().Register(slots, null!);

			Assert.IsType<InstructionToolbar>(Assert.Single(slots.For(Constants.Slots.TitleBarToolbar)).Create());
			Assert.IsType<CommandToggles>(Assert.Single(slots.For(Constants.Slots.CommandBar)).Create());
		});
	}
}
