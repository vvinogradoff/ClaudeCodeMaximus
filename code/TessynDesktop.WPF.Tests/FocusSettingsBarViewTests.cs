using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.WPF.Infrastructure;
using TessynDesktop.WPF.Rendering;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.State;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.ViewModels;
using TessynDesktop.WPF.Views;

namespace TessynDesktop.WPF.Tests;

/// <summary>
/// FR.6.24 / UC.DS.14 / TC.DS.41, TC.DS.43 to TC.DS.45 (the part a view test can check): the settings bar at the bottom of the Focus panel, the same <c>session.commandBar</c> slot as
/// the Session view's, bound to the same session pane; the Settings toggle in the Focus header between Send answers and Session, sharing the one remembered flag with the composer's gear.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class FocusSettingsBarViewTests
{
	private sealed class MemoryState : IClientStateStore
	{
		public ClientStateModel State { get; } = new();

		public Task LoadAsync() => Task.CompletedTask;

		public void Update(Action<ClientStateModel> change) => change(State);

		public Task FlushAsync() => Task.CompletedTask;
	}

	private readonly FakeTessynClient _client = new();
	private readonly MemoryState _state = new();

	private async Task<SessionPaneViewModel> OpenPaneAsync(bool open = true)
	{
		_client.Sessions["s1"] = FakeSession.WithEntries(2, "One");
		var pane = new SessionPaneViewModel(
			_client, new FakeTranscriptSurface(), new MarkdigEntryRenderer(new UrlPolicy()), new FakeUrlLauncher(), new ImmediateUiDispatcher(),
			new SwitchLatencyTracker(NullLogger<SwitchLatencyTracker>.Instance), NullLogger<SessionPaneViewModel>.Instance, Constants.Limits.MaxCachedTranscripts,
			new FocusViewModel(_client, new ImmediateUiDispatcher(), NullLogger<FocusViewModel>.Instance), null, new MediaPreviewPolicy(), _state);
		if (open)
			await pane.OpenAsync("s1", "One");
		pane.Efforts.Add(new SelectorOptionViewModel("low", "Low"));
		pane.Efforts.Add(new SelectorOptionViewModel("high", "High"));
		return pane;
	}

	private static ShellSlots Slots()
	{
		var slots = new ShellSlots();
		new TurnLoopShellModule().Register(slots, null!);
		new ToolbarShellModule().Register(slots, null!);
		return slots;
	}

	private sealed record Pair(FocusPanel Focus, SessionPane Session, SlotHost FocusBar, SlotHost SessionBar);

	private static Pair Create(SessionPaneViewModel pane, double width = 700)
	{
		var slots = Slots();
		var focus = new FocusPanel { DataContext = pane.Focus, Session = pane };
		var session = new SessionPane { DataContext = pane };
		Layout(session, width);   // applies the templates: the slot host is in the visual tree after that
		var focusBar = (SlotHost)focus.FindName("SettingsBar")!;
		var sessionBar = Descendants<SlotHost>(session).Single(h => h.SlotId == Constants.Slots.CommandBar);
		focusBar.Slots = slots;
		sessionBar.Slots = slots;
		focusBar.Populate();
		sessionBar.Populate();
		Layout(focus, width);
		Layout(session, width);
		return new Pair(focus, session, focusBar, sessionBar);
	}

	private static void Layout(FrameworkElement element, double width)
	{
		element.Measure(new Size(width, 700));
		element.Arrange(new Rect(0, 0, width, 700));
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

	private static ComboBox ComboNamed(DependencyObject root, string name) =>
		Descendants<ComboBox>(root).Single(c => AutomationProperties.GetName(c) == name);

	private static ToggleButton SessionGear(Pair v) => (ToggleButton)v.Session.FindName("SettingsToggle")!;

	private static ToggleButton FocusGear(Pair v) => (ToggleButton)v.Focus.FindName("SettingsToggle")!;

	private static void Click(ToggleButton button) =>
		((IToggleProvider)new ToggleButtonAutomationPeer(button).GetPattern(PatternInterface.Toggle)!).Toggle();

	[Fact]
	public async Task TheFocusPanel_ShowsTheSameBarAsTheSessionView_SameControlsSameOrder()   // TC.DS.41
	{
		var pane = await OpenPaneAsync();
		StaTestRunner.Run(() =>
		{
			var v = Create(pane);
			Assert.Equal(Visibility.Visible, v.FocusBar.Visibility);
			Assert.Equal(Grid.GetRow(v.FocusBar), ((Grid)v.Focus.Content).RowDefinitions.Count - 1);   // at the bottom, below the cards

			var types = (SlotHost h) => h.Items.Cast<object>().Select(i => i.GetType()).ToList();
			Assert.Equal(types(v.SessionBar), types(v.FocusBar));
			Assert.Equal(2, v.FocusBar.Items.Count);

			string[] Names(SlotHost h) => Descendants<FrameworkElement>(h).Select(AutomationProperties.GetName).Where(n => !string.IsNullOrEmpty(n)).ToArray();
			Assert.Equal(Names(v.SessionBar), Names(v.FocusBar));
			Assert.Contains("Model", Names(v.FocusBar));
			Assert.Contains("Streaming Session Mode", Names(v.FocusBar));

			// a control contributed to the slot appears in both
			var slots = Slots();
			slots.Add(new ShellSlotContribution { SlotId = Constants.Slots.CommandBar, Order = 99, Create = () => new Button { Content = "Extra", Name = "Extra" } });
			var focusBar = new SlotHost { SlotId = Constants.Slots.CommandBar, Slots = slots };
			var sessionBar = new SlotHost { SlotId = Constants.Slots.CommandBar, Slots = slots };
			focusBar.Populate();
			sessionBar.Populate();
			Assert.Equal(3, focusBar.Items.Count);
			Assert.Equal(3, sessionBar.Items.Count);
			Assert.NotSame(focusBar.Items[0], sessionBar.Items[0]);   // each host has its own view instances
		});
	}

	[Fact]
	public async Task APickInOneBar_ShowsInTheOtherAtOnce_BothActOnTheOneSession()   // TC.DS.42 (the part a view test can check)
	{
		var pane = await OpenPaneAsync();
		StaTestRunner.Run(() =>
		{
			var v = Create(pane);
			var focusEffort = ComboNamed(v.FocusBar, "Effort");
			var sessionEffort = ComboNamed(v.SessionBar, "Effort");
			Assert.NotSame(focusEffort, sessionEffort);

			focusEffort.SelectedItem = pane.Efforts.Single(e => e.Id == "high");
			Assert.Same(pane.SelectedEffort, pane.Efforts.Single(e => e.Id == "high"));
			Assert.Equal("high", ((SelectorOptionViewModel)sessionEffort.SelectedItem).Id);

			sessionEffort.SelectedItem = pane.Efforts.Single(e => e.Id == "low");
			Assert.Equal("low", ((SelectorOptionViewModel)focusEffort.SelectedItem).Id);

			pane.SelectedEffort = pane.Efforts.Single(e => e.Id == "high");   // from the model (a restore, a remote pick)
			Assert.Equal("high", ((SelectorOptionViewModel)focusEffort.SelectedItem).Id);
			Assert.Equal("high", ((SelectorOptionViewModel)sessionEffort.SelectedItem).Id);
		});
	}

	[Fact]
	public async Task TheSettingsToggle_SitsBetweenSendAnswersAndSession_WithTheComposersGearTooltipAndName()   // TC.DS.43
	{
		var pane = await OpenPaneAsync();
		StaTestRunner.Run(() =>
		{
			var v = Create(pane);
			var header = (DockPanel)v.Focus.FindName("HeaderRow")!;
			var send = (Button)v.Focus.FindName("SendAnswersButton")!;
			var session = (Button)v.Focus.FindName("SessionButton")!;
			var gear = FocusGear(v);
			var composer = SessionGear(v);

			double Left(FrameworkElement e) => e.TranslatePoint(default, header).X;
			Assert.True(Left(send) + send.ActualWidth <= Left(gear) + 0.5);
			Assert.True(Left(gear) + gear.ActualWidth <= Left(session) + 0.5);
			Assert.Equal(Visibility.Visible, gear.Visibility);

			Assert.Equal(composer.ToolTip, gear.ToolTip);
			Assert.Equal("Settings", AutomationProperties.GetName(gear));
			Assert.Equal(AutomationProperties.GetName(composer), AutomationProperties.GetName(gear));
			Assert.Equal(composer.Style.Setters.Count, gear.Style.Setters.Count);   // the PlainToggle style, loaded from the same dictionary
			Assert.Equal(composer.Style.Setters.OfType<Setter>().Select(x => x.Property), gear.Style.Setters.OfType<Setter>().Select(x => x.Property));
			Assert.Equal(composer.Padding, gear.Padding);
			var glyph = Descendants<TextBlock>(gear).Single();
			var composerGlyph = Descendants<TextBlock>(composer).Single();
			Assert.Equal(composerGlyph.Text, glyph.Text);
			Assert.Equal(composerGlyph.FontSize, glyph.FontSize);
			Assert.Equal(Descendants<TextBlock>(composer).Single().FontFamily.Source, glyph.FontFamily.Source);
		});
	}

	[Fact]
	public async Task TheSettingsToggle_IsShownWithNoSession_AndTheBarStaysShown()   // TC.DS.43, TC.DS.45
	{
		var pane = await OpenPaneAsync(open: false);
		StaTestRunner.Run(() =>
		{
			var v = Create(pane);
			Assert.Equal(Visibility.Visible, FocusGear(v).Visibility);
			Assert.True(FocusGear(v).ActualWidth > 0);
			Assert.Equal(Visibility.Visible, v.FocusBar.Visibility);   // as the Session view's bar is without a session
			Assert.Equal(v.SessionBar.Visibility, v.FocusBar.Visibility);
		});
	}

	[Fact]
	public async Task TheToggle_InFocus_HidesAndShowsBothBars_BothTogglesShowTheSameState_AndTheFlagIsRemembered()   // TC.DS.44
	{
		var pane = await OpenPaneAsync();
		StaTestRunner.Run(() =>
		{
			var v = Create(pane);
			Assert.True(FocusGear(v).IsChecked);
			Assert.True(SessionGear(v).IsChecked);

			Click(FocusGear(v));
			v.Focus.UpdateLayout();
			v.Session.UpdateLayout();
			Assert.False(pane.IsCommandBarVisible);
			Assert.Equal(Visibility.Collapsed, v.FocusBar.Visibility);
			Assert.Equal(Visibility.Collapsed, v.SessionBar.Visibility);
			Assert.False(FocusGear(v).IsChecked);
			Assert.False(SessionGear(v).IsChecked);
			Assert.False(_state.State.CommandBarVisible);   // the one remembered flag

			Click(SessionGear(v));   // the composer's gear flips the same flag
			Assert.True(pane.IsCommandBarVisible);
			Assert.Equal(Visibility.Visible, v.FocusBar.Visibility);
			Assert.Equal(Visibility.Visible, v.SessionBar.Visibility);
			Assert.True(FocusGear(v).IsChecked);
			Assert.True(SessionGear(v).IsChecked);
			Assert.True(_state.State.CommandBarVisible);

			Click(FocusGear(v));   // hidden again, and a restarted client starts hidden
			var restarted = new SessionPaneViewModel(
				_client, new FakeTranscriptSurface(), new MarkdigEntryRenderer(new UrlPolicy()), new FakeUrlLauncher(), new ImmediateUiDispatcher(),
				new SwitchLatencyTracker(NullLogger<SwitchLatencyTracker>.Instance), NullLogger<SessionPaneViewModel>.Instance, Constants.Limits.MaxCachedTranscripts,
				null, null, null, _state);
			Assert.False(restarted.IsCommandBarVisible);
		});
	}

	[Fact]
	public async Task TheSettingsToggle_NeverTakesTheKeyboardFocus()   // TC.DS.46 (the part a view test can check)
	{
		var pane = await OpenPaneAsync();
		StaTestRunner.Run(() =>
		{
			var v = Create(pane);
			Assert.False(FocusGear(v).Focusable);
			Assert.False(v.FocusBar.Focusable);
			Assert.False(v.FocusBar.IsTabStop);
		});
	}

	[Fact]
	public async Task ANarrowPanel_WrapsTheBar_AndTheCardsAreaStaysAboveIt()   // TC.DS.47 (the look stays a manual check)
	{
		var pane = await OpenPaneAsync();
		StaTestRunner.Run(() =>
		{
			var wide = Create(pane, 900);
			var wideHeight = wide.FocusBar.ActualHeight;
			var narrow = Create(pane, 320);
			Assert.True(narrow.FocusBar.ActualHeight > wideHeight, "the controls wrap onto more lines");
			var scroll = Descendants<ScrollViewer>(narrow.Focus).First();
			Assert.True(scroll.TranslatePoint(default, narrow.Focus).Y + scroll.ActualHeight <= narrow.FocusBar.TranslatePoint(default, narrow.Focus).Y + 0.5);
		});
	}
}
