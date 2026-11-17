using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.Client;
using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Infrastructure;
using TessynDesktop.WPF.Rendering;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.State;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.ViewModels;
using TessynDesktop.WPF.Views;

namespace TessynDesktop.WPF.Tests;

/// <summary>The window-chrome feedback: the status bar's meters take the width, the Recent dropdown fills the title bar's middle and names the open session, and Import and Settings are menu entries.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class ChromeFeedbackTests : IDisposable
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

	private readonly FakeTessynClient _client = new();
	private readonly FakeTranscriptSurface _surface = new();
	private readonly ImmediateUiDispatcher _ui = new();
	private readonly MainViewModel _main;
	private readonly SessionListsHost _host;

	public ChromeFeedbackTests()
	{
		var b = new TreeBuilder();
		_client.Directories = [b.Directory("d1", "Repo", [], b.Session("s1", "First one"), b.Session("s2", "Second one"))];
		foreach (var id in new[] { "s1", "s2" })
			_client.Sessions[id] = FakeSession.WithEntries(2, "Session " + id);

		var locator = new ScriptedLocator();
		locator.Results.Enqueue(new DaemonDiscoveryResult(DaemonDiscoveryStatus.Found, new DaemonEndpoint(new Uri("http://127.0.0.1:1/tdcp"), "t", "r", 1)));
		var connection = new ConnectionViewModel(locator, new FixedClientFactory(_client), _ui, NullLogger<ConnectionViewModel>.Instance, TimeSpan.FromMilliseconds(5));
		_main = new MainViewModel(
			connection,
			c => new TreeViewModel(c, _ui, NullLogger<TreeViewModel>.Instance, TimeSpan.Zero),
			c => new SessionPaneViewModel(c, _surface, new MarkdigEntryRenderer(new UrlPolicy()), new FakeUrlLauncher(), _ui,
				new SwitchLatencyTracker(NullLogger<SwitchLatencyTracker>.Instance), NullLogger<SessionPaneViewModel>.Instance),
			new MemoryState(),
			NullLogger<MainViewModel>.Instance);
		_host = new SessionListsHost(connection, _main, _ui, NullLoggerFactory.Instance);
		_ = _main.StartAsync(CancellationToken.None);
	}

	public void Dispose()
	{
		_host.Recent.Dispose();
		_host.Dispose();
	}

	private static void Layout(FrameworkElement element, double width, double height)
	{
		element.Measure(new Size(width, height));
		element.Arrange(new Rect(0, 0, width, height));
		element.UpdateLayout();
	}

	[Fact]
	public void TheRecentButton_SaysRecent_UntilASessionIsOpen_ThenItsName_AndFollowsTheTitle()
	{
		Assert.Equal(Constants.SessionLists.RecentTabTitle, _host.Recent.ButtonText);

		_main.Tree!.SelectNodeById("s1");
		Assert.Equal("First one", _host.Recent.ButtonText);

		_main.Tree.SelectNodeById("s2");
		Assert.Equal("Second one", _host.Recent.ButtonText);

		_main.Session!.Close();
		Assert.Equal(Constants.SessionLists.RecentTabTitle, _host.Recent.ButtonText);
	}

	[Fact]
	public void TheRecentDropdown_FillsTheWidthItIsGiven_AndShowsTheOpenSessionsName()
	{
		var listener = new BindingErrors();
		StaTestRunner.Run(() =>
		{
			PresentationTraceSources.Refresh();
			PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
			PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;
			var dropdown = new RecentDropdown { DataContext = _host.Recent, HorizontalAlignment = HorizontalAlignment.Stretch };
			Layout(dropdown, 700, 40);

			var toggle = (ToggleButton)dropdown.FindName("Toggle")!;
			Assert.True(toggle.ActualWidth > 650, $"the dropdown button is {toggle.ActualWidth} wide in 700");
			Assert.Equal(Constants.SessionLists.RecentTabTitle, ((TextBlock)dropdown.FindName("Label")!).Text);

			_main.Tree!.SelectNodeById("s2");
			dropdown.UpdateLayout();
			Assert.Equal("Second one", ((TextBlock)dropdown.FindName("Label")!).Text);
			PresentationTraceSources.DataBindingSource.Listeners.Remove(listener);
		});

		Assert.Empty(listener.Messages);
	}

	[Fact]
	public void TheTitleBarsMiddleSlot_GivesTheRecentDropdownAllTheSpaceBetweenToolbarAndBuildStamp()
	{
		StaTestRunner.Run(() =>
		{
			var store = new ClientSettingsStore(new MemoryClientState(), NullLogger<ClientSettingsStore>.Instance);
			var commands = new ShellCommands(store, NullLogger<ShellCommands>.Instance);
			var slots = new ShellSlots();
			new SessionListsModule(_host).Register(slots, commands);
			var titleBar = new TitleBarViewModel(new AppearanceService(store, new FixedProbe()), store, commands, _ui, "202611.17.0900", false);
			var bar = new TitleBar { DataContext = titleBar };
			var center = (SlotHost)bar.FindName("CenterSlot")!;
			center.Slots = slots;
			center.Logger = NullLogger.Instance;
			center.Populate();
			Layout(bar, 1200, 40);

			var dropdown = center.Items.OfType<RecentDropdown>().Single();
			Assert.True(dropdown.ActualWidth > 600, $"the Recent dropdown is {dropdown.ActualWidth} wide in a 1200 title bar");
			Assert.Empty(slots.For(Constants.Slots.TitleBarToolbar));
		});
	}

	[Fact]
	public void TheStatusBarsMeters_TakeAllTheWidthTheyAreGiven()
	{
		StaTestRunner.Run(() =>
		{
			var ui = new ImmediateUiDispatcher();
			var connection = new ConnectionViewModel(new ScriptedLocator(), new FixedClientFactory(new FakeTessynClient()), ui, NullLogger<ConnectionViewModel>.Instance, TimeSpan.FromMilliseconds(5));
			var bar = new StatusBarViewModel(connection, _main, ui, NullLogger<StatusBarViewModel>.Instance) { HasUsage = true, FiveHourPercent = 30, SevenDayPercent = 60 };
			var view = new StatusBarItems { DataContext = bar };
			Layout(view, 1000, 26);

			var five = (ProgressBar)view.FindName("FiveHourMeter")!;
			var seven = (ProgressBar)view.FindName("SevenDayMeter")!;
			Assert.True(five.ActualWidth > 800, $"the 5-hour meter is {five.ActualWidth} wide in 1000");
			Assert.Equal(five.ActualWidth, seven.ActualWidth);
			Assert.Equal(26, ((FrameworkElement)view.FindName("Meters")!).ActualHeight);   // two 13 px bars: they fill a 26 px bar edge to edge
		});
	}

	[Fact]
	public void ImportAndSettings_AreEntriesOfTheMainMenu_ImportFirst()
	{
		var store = new ClientSettingsStore(new MemoryClientState(), NullLogger<ClientSettingsStore>.Instance);
		var commands = new ShellCommands(store, NullLogger<ShellCommands>.Instance);
		var slots = new ShellSlots();
		new ImportShellModule(null!, new TreeContext()).Register(slots, commands);
		new SettingsShellModule(null!).Register(slots, commands);

		var entries = commands.BuildMenu(Constants.Menus.Main, null);

		Assert.Equal([Constants.Import.OpenCommandId, Constants.Settings.OpenCommandId], entries.Select(e => e.CommandId));
		Assert.Empty(slots.For(Constants.Slots.TitleBarToolbar));
		StaTestRunner.Run(() =>
		{
			var menu = CommandMenu.Build(commands, Constants.Menus.Main, null)!;
			Assert.Equal([Constants.Import.OpenCommandTitle, Constants.Settings.OpenCommandTitle], menu.Items.OfType<MenuItem>().Select(m => (string)m.Header));
		});
	}

	[Fact]
	public void TheProfilesButton_IsInTheStatusBarsRightSlot_NotTheFillingOne()
	{
		var store = new ClientSettingsStore(new MemoryClientState(), NullLogger<ClientSettingsStore>.Instance);
		var commands = new ShellCommands(store, NullLogger<ShellCommands>.Instance);
		var slots = new ShellSlots();
		var ui = new ImmediateUiDispatcher();
		var connection = new ConnectionViewModel(new ScriptedLocator(), new FixedClientFactory(_client), ui, NullLogger<ConnectionViewModel>.Instance, TimeSpan.FromMilliseconds(5));
		var bar = new StatusBarViewModel(connection, _main, ui, NullLogger<StatusBarViewModel>.Instance);
		var appearance = new AppearanceService(store, new FixedProbe());
		new ChromeShellModule(bar, new TitleBarViewModel(appearance, store, commands, ui, "x", false), _main, appearance).Register(slots, commands);

		Assert.Single(slots.For(Constants.Slots.StatusBarItems));
		Assert.Empty(slots.For(Constants.Slots.StatusBarRight));
	}

	private sealed class FixedProbe : ISystemThemeProbe
	{
		public bool IsLight => true;

		public event EventHandler? Changed
		{
			add { }
			remove { }
		}
	}
}
