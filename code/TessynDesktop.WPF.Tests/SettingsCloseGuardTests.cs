using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.Client;
using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Infrastructure;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.State;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.ViewModels;
using TessynDesktop.WPF.ViewModels.Settings;
using TessynDesktop.WPF.Views;

namespace TessynDesktop.WPF.Tests;

/// <summary>
/// FR.4.7 (UC.ST.01, TC.ST.01-06; TC.ST.07 is the end-to-end test in the E2E project, and UC.CL.05 A3 / TC.CL.33 are the pending-colours case): closing the Settings window with unsaved
/// changes in any tab asks Save, Discard or Cancel; with none it closes at once.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class SettingsCloseGuardTests
{
	private readonly FakeTessynClient _client = new FakeTessynClient().AnnouncingSettings().WithSettingsValues();
	private readonly FakeDialogPresenter _presenter = new();
	private readonly ClientSettingsStore _store = SettingsTestSupport.NewStore();

	private async Task<SettingsViewModel> LoadedAsync()
	{
		var settings = SettingsTestSupport.NewSettings(_client, _store, presenter: _presenter);
		await settings.ModelsProfiles.Profiles.AttachAsync(_client);
		await settings.InitializeAsync();
		return settings;
	}

	/// <summary>The dialog's answer: the id of a choice ("save", "discard"), or null for Cancel.</summary>
	private void Answer(string? choiceId) => _presenter.Answer = dialog =>
	{
		if (choiceId == null)
			dialog.CancelCommand.Execute(null);
		else
			dialog.ChooseCommand.Execute(dialog.Choices.Single(c => c.Id == choiceId));

		return Task.CompletedTask;
	};

	private static void EditAppearance(SettingsViewModel settings) => settings.Appearance.UserFontSize = "21";

	private static void PickColour(SettingsViewModel settings, string hex = "#123456") => settings.Colours.Rows.Single(r => r.Key == ColourCatalog.Text).Set(hex);

	// ---- TC.ST.01 ----

	[Fact]
	public async Task NoUnsavedChanges_CloseAsksNothing_AndCloses()
	{
		var settings = await LoadedAsync();

		Assert.False(settings.HasChanges);
		Assert.Empty(settings.UnsavedTabs);
		Assert.True(await settings.ConfirmCloseAsync());
		Assert.Empty(_presenter.Shown);
	}

	// ---- TC.ST.02 ----

	[Fact]
	public async Task TheDialog_NamesTheTabsWithUnsavedChanges_PendingColoursIncluded()
	{
		var settings = await LoadedAsync();
		Answer(null);
		EditAppearance(settings);
		PickColour(settings);

		Assert.Equal(["Appearance", "Colours"], settings.UnsavedTabs);
		Assert.False(await settings.ConfirmCloseAsync());

		var dialog = Assert.Single(_presenter.Shown);
		Assert.Equal("Unsaved changes in Appearance, Colours.", dialog.Message);
		Assert.Equal(["Save", "Discard"], dialog.Choices.Select(c => c.Label));
		Assert.Equal("Cancel", dialog.CancelText);

		// one tab only, and a daemon tab
		_presenter.Shown.Clear();
		var other = await LoadedAsync();
		other.General.HttpsProxy = "http://127.0.0.1:1";
		Answer(null);
		await other.ConfirmCloseAsync();
		Assert.Equal("Unsaved changes in General.", Assert.Single(_presenter.Shown).Message);
	}

	[Fact]
	public async Task ColoursOnly_PendingColour_IsEnoughToAsk()
	{
		var settings = await LoadedAsync();
		Answer(null);
		PickColour(settings);

		Assert.False(await settings.ConfirmCloseAsync());
		Assert.Equal("Unsaved changes in Colours.", Assert.Single(_presenter.Shown).Message);
	}

	// ---- TC.ST.03 ----

	[Fact]
	public async Task Save_SavesEveryChangedTab_AndCloses_AndTheColourIsStored()
	{
		var settings = await LoadedAsync();
		Answer("save");
		EditAppearance(settings);
		PickColour(settings);
		settings.General.HttpsProxy = "http://127.0.0.1:2";

		Assert.True(await settings.ConfirmCloseAsync());

		Assert.Equal("#123456", _store.Get<ColourSettings>().Light[ColourCatalog.Text]);
		Assert.Equal(21, _store.Get<AppearanceSettings>().UserFontSize);
		var (section, patch, _) = Assert.Single(_client.SettingsUpdates);
		Assert.Equal("general", section);
		Assert.Equal("http://127.0.0.1:2", Assert.IsType<GeneralSettingsPatch>(patch).HttpsProxy);
		Assert.False(settings.HasChanges);
	}

	[Fact]
	public async Task Save_ThatFails_KeepsTheWindowOpen_AndShowsTheError()
	{
		var settings = await LoadedAsync();
		Answer("save");
		settings.General.OllamaBaseUrl = "ftp://nope";
		_client.SettingsUpdateException = new TessynRpcException(RemoteProtocol.ErrorCodes.InvalidParams, "ollamaBaseUrl must be an http(s) URL");
		PickColour(settings);

		Assert.False(await settings.ConfirmCloseAsync());   // the window stays

		Assert.True(settings.HasError);   // and shows that not everything was saved
		Assert.Contains("must be an http(s) URL", settings.General.ErrorText);
		Assert.Equal("ftp://nope", settings.General.OllamaBaseUrl);   // the edit is kept
		Assert.True(settings.HasChanges);
	}

	// ---- TC.ST.04 ----

	[Fact]
	public async Task Discard_StoresNothing_AndCloses()
	{
		var settings = await LoadedAsync();
		Answer("discard");
		EditAppearance(settings);
		PickColour(settings);
		settings.General.HttpsProxy = "http://127.0.0.1:3";

		Assert.True(await settings.ConfirmCloseAsync());

		Assert.Empty(_store.Get<ColourSettings>().Light);
		Assert.NotEqual(21, _store.Get<AppearanceSettings>().UserFontSize);
		Assert.Empty(_client.SettingsUpdates);
	}

	[Fact]
	public async Task Cancel_KeepsTheWindowAndTheEditsAsTheyWere()
	{
		var settings = await LoadedAsync();
		Answer(null);
		EditAppearance(settings);
		PickColour(settings);

		Assert.False(await settings.ConfirmCloseAsync());

		Assert.True(settings.HasChanges);
		Assert.Equal("21", settings.Appearance.UserFontSize);
		Assert.True(settings.Colours.Rows.Single(r => r.Key == ColourCatalog.Text).IsPending);
		Assert.Empty(_store.Get<ColourSettings>().Light);
		Assert.Empty(_client.SettingsUpdates);
	}

	// ---- TC.ST.05: every way of closing goes through the guard ----

	private static void Pump(Dispatcher dispatcher)
	{
		var frame = new DispatcherFrame();
		dispatcher.BeginInvoke(DispatcherPriority.Background, () => frame.Continue = false);
		Dispatcher.PushFrame(frame);
	}

	[Fact]
	public async Task TheCloseButton_Esc_AndTheClosingEvent_AllAskFirst()
	{
		var settings = await LoadedAsync();
		PickColour(settings);

		StaTestRunner.Run(() =>
		{
			var window = new SettingsWindow(settings);
			var closed = false;
			window.Closed += (_, _) => closed = true;
			window.ShowActivated = false;
			window.WindowState = WindowState.Minimized;   // a unit test must never take the foreground
			window.Show();
			try
			{
				var closeButton = (Button)window.FindName("CloseButton");
				Assert.True(closeButton.IsCancel);   // Esc clicks it

				Answer(null);
				closeButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));   // the Close button and Esc
				Pump(window.Dispatcher);
				Assert.False(closed);
				Assert.Equal(1, _presenter.Shown.Count);

				window.Close();   // the title bar's close button and Alt+F4 raise the same Closing event
				Pump(window.Dispatcher);
				Assert.False(closed);
				Assert.Equal(2, _presenter.Shown.Count);
				Assert.True(settings.HasChanges);

				Answer("discard");
				window.Close();
				Pump(window.Dispatcher);
				Assert.True(closed);
				Assert.Equal(3, _presenter.Shown.Count);
			}
			finally
			{
				if (window.IsVisible)
					window.Close();
			}
		});
	}

	[Fact]
	public async Task NoEdits_TheWindowCloses_WithoutAnyDialog()
	{
		var settings = await LoadedAsync();
		StaTestRunner.Run(() =>
		{
			var window = new SettingsWindow(settings);
			var closed = false;
			window.Closed += (_, _) => closed = true;
			window.ShowActivated = false;
			window.WindowState = WindowState.Minimized;
			window.Show();
			((Button)window.FindName("CloseButton")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
			Pump(window.Dispatcher);
			Assert.True(closed);
		});
		Assert.Empty(_presenter.Shown);
	}

	[Fact]
	public async Task Save_ThroughTheWindow_StoresTheColour_AndTheWindowCloses_UC_CL_05_A3()
	{
		var settings = await LoadedAsync();
		PickColour(settings);
		Answer("save");

		StaTestRunner.Run(() =>
		{
			var window = new SettingsWindow(settings);
			var closed = false;
			window.Closed += (_, _) => closed = true;
			window.ShowActivated = false;
			window.WindowState = WindowState.Minimized;
			window.Show();
			window.Close();
			Pump(window.Dispatcher);
			Assert.True(closed);
		});

		Assert.Equal("#123456", _store.Get<ColourSettings>().Light[ColourCatalog.Text]);   // TC.CL.33: the pending colour is applied through the guard
		Assert.Equal("Unsaved changes in Colours.", Assert.Single(_presenter.Shown).Message);
	}

	// ---- TC.ST.06: the main window closing ----

	private SettingsHost HostWith(SettingsWindow window)
	{
		var ui = new ImmediateUiDispatcher();
		var connection = new ConnectionViewModel(new ScriptedLocator(), new FixedClientFactory(new FakeTessynClient()), ui, NullLogger<ConnectionViewModel>.Instance, TimeSpan.FromMilliseconds(5));
		var host = new SettingsHost(connection, _ => throw new InvalidOperationException("not used"), new DialogService(_presenter, ui), NullLogger<SettingsHost>.Instance);
		typeof(SettingsHost).GetField("_window", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(host, window);   // an open window, without a daemon connection
		return host;
	}

	[Fact]
	public async Task TheMainWindowClosing_AsksThroughTheHost_CancelKeepsBoth_SaveOrDiscardLetsThemClose()
	{
		var settings = await LoadedAsync();
		PickColour(settings);

		StaTestRunner.Run(() =>
		{
			var window = new SettingsWindow(settings);
			var host = HostWith(window);
			var closed = false;
			window.Closed += (_, _) => closed = true;
			window.ShowActivated = false;
			window.WindowState = WindowState.Minimized;
			window.Show();
			try
			{
				Assert.True(host.HasUnsavedChanges);

				Answer(null);   // Cancel: the main window must not close
				Assert.False(host.ConfirmCloseAsync().GetAwaiter().GetResult());
				Assert.True(window.IsVisible);
				Assert.True(settings.HasChanges);

				Answer("save");   // Save: the main window closes, and the Settings window with it, without asking again
				Assert.True(host.ConfirmCloseAsync().GetAwaiter().GetResult());
				Assert.Equal("#123456", _store.Get<ColourSettings>().Light[ColourCatalog.Text]);
				Assert.False(host.HasUnsavedChanges);
				var asked = _presenter.Shown.Count;
				window.Close();   // closed by its owner
				Pump(window.Dispatcher);
				Assert.True(closed);
				Assert.Equal(asked, _presenter.Shown.Count);
			}
			finally
			{
				if (window.IsVisible)
					window.Close();
			}
		});
	}

	[Fact]
	public async Task TheHost_WithNoWindowOrNoEdits_LetsTheMainWindowCloseAtOnce()
	{
		var settings = await LoadedAsync();
		StaTestRunner.Run(() =>
		{
			var window = new SettingsWindow(settings);
			var host = HostWith(window);
			Assert.False(host.HasUnsavedChanges);
			Assert.True(host.ConfirmCloseAsync().GetAwaiter().GetResult());
			Assert.Empty(_presenter.Shown);
		});
	}
}
