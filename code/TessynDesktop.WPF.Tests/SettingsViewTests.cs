using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.ViewModels;
using TessynDesktop.WPF.ViewModels.Settings;
using TessynDesktop.WPF.Views;
using TessynDesktop.WPF.Views.Settings;

namespace TessynDesktop.WPF.Tests;

/// <summary>The Settings window and its tabs (W6a, W6b) on an STA thread: every tab binds to its view model without binding errors, secrets never reach a text box, and the shell module and host behave.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class SettingsViewTests
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

	private static List<T> FindAll<T>(DependencyObject root) where T : DependencyObject
	{
		var found = new List<T>();
		var count = VisualTreeHelper.GetChildrenCount(root);
		for (var i = 0; i < count; i++)
		{
			var child = VisualTreeHelper.GetChild(root, i);
			if (child is T match)
				found.Add(match);

			found.AddRange(FindAll<T>(child));
		}

		return found;
	}

	/// <summary>Not collapsed itself or through a parent (IsVisible needs a presentation source, which a unit test does not have).</summary>
	private static bool IsShown(DependencyObject element)
	{
		for (var current = element; current != null; current = VisualTreeHelper.GetParent(current))
			if (current is UIElement { Visibility: not Visibility.Visible })
				return false;

		return true;
	}

	/// <summary>A window that was never shown has no visual tree of its own; its content is laid out directly (it inherits the window's DataContext through the logical tree).</summary>
	private static FrameworkElement ContentOf(Window window) => (FrameworkElement)window.Content;

	private static void Layout(FrameworkElement element, double width = 800, double height = 700)
	{
		if (element is Window window)
			element = ContentOf(window);

		element.Measure(new Size(width, height));
		element.Arrange(new Rect(0, 0, width, height));
		element.UpdateLayout();
	}

	private static void WithoutBindingErrors(Action body)
	{
		var listener = new BindingErrors();
		StaTestRunner.Run(() =>
		{
			PresentationTraceSources.Refresh();
			PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
			PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;
			try
			{
				body();
			}
			finally
			{
				PresentationTraceSources.DataBindingSource.Listeners.Remove(listener);
			}
		});

		Assert.Empty(listener.Messages);
	}

	private static FakeTessynClient RichClient()
	{
		var client = new FakeTessynClient().AnnouncingSettings().WithSettingsValues();
		client.McpServers =
		[
			new McpServerDto("a", "Linear", true, McpTransportDto.Http, "https://a.example.com/mcp", true, null, false, null, McpAuthTypeDto.Oauth2, null, false,
				new McpOAuthDto("https://auth/a", "https://auth/t", null, "cid", HasClientSecret: true, "read", true), false, McpServerStatusDto.NotConnected, "me"),
			new McpServerDto("b", "Keyed", false, McpTransportDto.Http, "https://b.example.com/mcp", false, null, false, null, McpAuthTypeDto.StaticHeader, "Authorization", HasStaticHeaderValue: true, null, false, McpServerStatusDto.NotApplicable, null),
			new McpServerDto("c", "Local", true, McpTransportDto.Stdio, null, false, "npx", true, @"C:\w", McpAuthTypeDto.None, null, false, null, false, McpServerStatusDto.NotApplicable, null),
		];
		client.TelegramStatus = new TelegramStatusDto(true, true, HasToken: true, LastError: "Could not reach Telegram");
		client.RelayStatus = new RelayStatusDto(true, RelayStateDto.Connected, "https://tessyn.example.com", HasToken: true, DateTimeOffset.UtcNow, LastError: null);
		client.Profiles = [new("default", "[Sys] me@example.com", true, "me@example.com", ProfileAuthState.Unknown, true), new("p1", "Work", false, null, ProfileAuthState.LoggedIn, false)];
		return client;
	}

	private static async Task<SettingsViewModel> LoadedSettingsAsync(FakeTessynClient client, ScriptedMcpEditorPresenter? editors = null)
	{
		var settings = SettingsTestSupport.NewSettings(client, editor: editors);
		await settings.ModelsProfiles.Profiles.AttachAsync(client);
		await settings.InitializeAsync();
		return settings;
	}

	[Fact]
	public async Task EveryTab_BindsWithoutBindingErrors()
	{
		var settings = await LoadedSettingsAsync(RichClient());

		WithoutBindingErrors(() =>
		{
			var tabs = new FrameworkElement[]
			{
				new GeneralSettingsView { DataContext = settings.General },
				new NotificationsSettingsView { DataContext = settings.Notifications },
				new TelegramSettingsView { DataContext = settings.Telegram },
				new WebAccessSettingsView { DataContext = settings.WebAccess },
				new McpSettingsView { DataContext = settings.Mcp },
				new ModelsProfilesSettingsView { DataContext = settings.ModelsProfiles },
				new AppearanceSettingsView { DataContext = settings.Appearance },
				new ColoursSettingsView { DataContext = settings.Colours },
				new ScreensaverSettingsView { DataContext = settings.Screensaver },
				new KeyboardSettingsView { DataContext = settings.Keyboard },
			};
			foreach (var tab in tabs)
				Layout(tab);

			Assert.Equal(3, FindAll<ListBoxItem>(tabs[4]).Count);   // the MCP rows
			Assert.Equal(3, FindAll<TextBox>(tabs[8]).Count);       // one box per registered command
		});
	}

	[Fact]
	public async Task TheWindow_BindsWithoutBindingErrors_AndHasOneTabPerSection()
	{
		var settings = await LoadedSettingsAsync(RichClient());

		WithoutBindingErrors(() =>
		{
			var window = new SettingsWindow(settings);
			Layout(window, 880, 720);

			var tabs = FindAll<TabControl>(ContentOf(window)).Single();
			Assert.Equal(
				["General", "Notifications", "Telegram", "Web access", "MCP servers", "Models and profiles", "Appearance", "Colours", "Screensaver", "Startup", "Keyboard shortcuts"],
				tabs.Items.OfType<TabItem>().Select(t => (string)t.Header));

			// Every tab, one after the other: a tab's content is only built when it is selected.
			for (var i = 0; i < tabs.Items.Count; i++)
			{
				tabs.SelectedIndex = i;
				Layout(window, 880, 720);
			}
		});
	}

	[Fact]
	public async Task TheImportBanner_IsShownUntilTheImportWasDone()
	{
		var settings = await LoadedSettingsAsync(RichClient());
		Assert.True(settings.ImportOffered);

		WithoutBindingErrors(() =>
		{
			var window = new SettingsWindow(settings);
			Layout(window, 880, 720);

			Assert.Contains(FindAll<Button>(ContentOf(window)), b => b.Content as string == "Import from Avalonia" && IsShown(b));
		});
	}

	[Fact]
	public async Task ASectionWithoutItsCapability_ShowsTheNote_AndHidesTheControls()
	{
		var client = new FakeTessynClient().AnnouncingSettings(RemoteProtocol.ProfilesAdmin.Capability);
		var settings = await LoadedSettingsAsync(client);

		WithoutBindingErrors(() =>
		{
			var general = new GeneralSettingsView { DataContext = settings.General };
			Layout(general);

			var texts = FindAll<TextBlock>(general).Where(IsShown).Select(t => t.Text).ToList();
			Assert.Contains(texts, t => t.Contains("does not offer", StringComparison.Ordinal));
			Assert.DoesNotContain(FindAll<TextBox>(general), IsShown);
		});
	}

	[Fact]
	public async Task Secrets_NeverReachAnyTextBoxOrPasswordBox()
	{
		var client = RichClient();
		var settings = await LoadedSettingsAsync(client);

		WithoutBindingErrors(() =>
		{
			var views = new FrameworkElement[] { new TelegramSettingsView { DataContext = settings.Telegram }, new WebAccessSettingsView { DataContext = settings.WebAccess } };
			foreach (var view in views)
			{
				Layout(view);
				Assert.All(FindAll<PasswordBox>(view), box => Assert.Equal(string.Empty, box.Password));
			}

			var oauth = new McpServerEditorViewModel(client, client.McpServers[0], NullLogger.Instance);
			var keyed = new McpServerEditorViewModel(client, client.McpServers[1], NullLogger.Instance);
			foreach (var editor in new[] { oauth, keyed })
			{
				var window = new McpServerEditorWindow(editor);
				Layout(window, 560, 640);
				Assert.All(FindAll<PasswordBox>(ContentOf(window)), box => Assert.Equal(string.Empty, box.Password));
			}
		});
	}

	[Fact]
	public async Task APasswordBox_FeedsTheViewModel_AndIsEmptiedWhenTheViewModelClearsIt()
	{
		var settings = await LoadedSettingsAsync(RichClient());

		WithoutBindingErrors(() =>
		{
			var view = new TelegramSettingsView { DataContext = settings.Telegram };
			Layout(view);
			var box = FindAll<PasswordBox>(view).Single();

			box.Password = "123456:ABCdefGhIJKlmNoPQRsTUVwxyZ";
			Assert.Equal("123456:ABCdefGhIJKlmNoPQRsTUVwxyZ", settings.Telegram.TokenInput);

			settings.Telegram.TokenInput = string.Empty;
			Assert.Equal(string.Empty, box.Password);
		});
	}

	[Fact]
	public void TheEditorWindow_FollowsTheChosenTransportAndAuthentication_WithoutBindingErrors()
	{
		var editor = new McpServerEditorViewModel(RichClient(), null, NullLogger.Instance);

		WithoutBindingErrors(() =>
		{
			var window = new McpServerEditorWindow(editor);
			Layout(window, 560, 640);
			Assert.True(editor.IsHttp);
			Assert.False(editor.ShowOauth);

			editor.AuthType = McpAuthTypeDto.Oauth2;
			Layout(window, 560, 640);
			Assert.True(editor.ShowOauth);
			Assert.False(editor.ShowStaticHeader);

			editor.Transport = McpTransportDto.Stdio;
			Layout(window, 560, 640);
			Assert.True(editor.IsStdio);
			Assert.False(editor.ShowOauth);   // OAuth belongs to HTTP servers only
		});
	}

	[Fact]
	public void TheEditorWindow_ClosesItself_WhenTheServerWasSaved()
	{
		var client = RichClient();
		var editor = new McpServerEditorViewModel(client, null, NullLogger.Instance) { DisplayName = "New", Url = "https://n.example.com/mcp" };

		var closed = false;
		StaTestRunner.Run(() =>
		{
			var window = new McpServerEditorWindow(editor);
			window.Closed += (_, _) => closed = true;
			window.ShowActivated = false;
			window.WindowState = WindowState.Minimized;   // a unit test must never take the foreground
			window.Show();
			try
			{
				editor.SaveCommand.Execute(null);
				// The save completes on this thread synchronously (the fake answers at once); let the dispatcher run the queued close.
				var frame = new System.Windows.Threading.DispatcherFrame();
				window.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, () => frame.Continue = false);
				System.Windows.Threading.Dispatcher.PushFrame(frame);
			}
			finally
			{
				if (window.IsVisible)
					window.Close();
			}
		});

		Assert.True(editor.Saved);
		Assert.True(closed);
	}

	[Fact]
	public void TheShellModule_RegistersTheCommandAndTheTitleBarButton()
	{
		var store = SettingsTestSupport.NewStore();
		var commands = new ShellCommands(store, NullLogger<ShellCommands>.Instance);
		var slots = new ShellSlots();
		var ui = new ImmediateUiDispatcher();
		var connection = new ConnectionViewModel(new ScriptedLocator(), new FixedClientFactory(new FakeTessynClient()), ui, NullLogger<ConnectionViewModel>.Instance, TimeSpan.FromMilliseconds(5));
		var host = new SettingsHost(connection, _ => throw new InvalidOperationException("not used"), new DialogService(new FakeDialogPresenter(), ui), NullLogger<SettingsHost>.Instance);

		new SettingsShellModule(host).Register(slots, commands);

		var command = commands.Find(Constants.Settings.OpenCommandId);
		Assert.NotNull(command);
		Assert.Equal("Ctrl+OemComma", commands.GetGesture(Constants.Settings.OpenCommandId));
		Assert.True(command.AllowInTextInput);
		var contribution = Assert.Single(slots.For(Constants.Slots.TitleBarToolbar));
		StaTestRunner.Run(() =>
		{
			var button = Assert.IsType<Button>(contribution.Create());
			Assert.Equal(Constants.Settings.OpenButtonText, button.Content);
		});

		// The default gesture is a valid one and reaches the command.
		Assert.True(GestureText.TryParse(Constants.Settings.OpenCommandGesture, out var key, out var modifiers));
		Assert.Same(command, commands.FindByGesture(key, modifiers));
	}

	[Fact]
	public async Task Host_BeforeTheConnectionIsUp_TellsTheUserInsteadOfOpeningAnEmptyWindow()
	{
		var presenter = new FakeDialogPresenter();
		var ui = new ImmediateUiDispatcher();
		var connection = new ConnectionViewModel(new ScriptedLocator(), new FixedClientFactory(new FakeTessynClient()), ui, NullLogger<ConnectionViewModel>.Instance, TimeSpan.FromMilliseconds(5));
		var host = new SettingsHost(connection, _ => throw new InvalidOperationException("must not be built"), new DialogService(presenter, ui), NullLogger<SettingsHost>.Instance);

		await host.OpenAsync();

		Assert.False(host.IsOpen);
		var shown = Assert.Single(presenter.Shown);
		Assert.Equal(Constants.Settings.NotConnected, shown.Message);
	}
}
