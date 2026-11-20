using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.Client;
using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Rendering;
using TessynDesktop.WPF.Infrastructure;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.State;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.ViewModels;
using TessynDesktop.WPF.ViewModels.Settings;
using TessynDesktop.WPF.Views;

namespace TessynDesktop.WPF.Tests;

/// <summary>
/// FR.9.13 / TC.RD.01 to TC.RD.04: the closed Recent dropdown shows the open session's recency colour behind its name, switchable in Settings, Appearance ("Show recency colour in
/// the closed Recent dropdown", on by default) and applied live. The tests drive the real button with real layout, the settings tab, and the host that follows the open session.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class RecentDropdownRecencyTests : IDisposable
{
	private static readonly Color Tier1 = Color.FromRgb(0x11, 0x99, 0x33);
	private static readonly Color Tier4 = Color.FromRgb(0x99, 0x77, 0x11);

	private readonly FakeTessynClient _client = new();
	private readonly ImmediateUiDispatcher _ui = new();
	private readonly ClientSettingsStore _store = SettingsTestSupport.NewStore();
	private readonly MainViewModel _main;
	private readonly SessionListsHost _host;
	private readonly ListDisplayService _display;

	public RecentDropdownRecencyTests()
	{
		var now = DateTimeOffset.UtcNow;
		var b = new TreeBuilder();
		TreeSessionDto Session(string id, string name, TimeSpan? age) => b.SessionWith(
			new SessionStateDto(id, false, false, null, false, age == null ? null : now - age, age == null ? null : now - age, false, null), name);
		_client.Directories = [b.Directory("d1", "Repo", [], Session("fresh", "Fresh one", TimeSpan.FromMinutes(2)), Session("old", "Old one", TimeSpan.FromDays(3)), Session("quiet", "Quiet one", TimeSpan.FromHours(3)))];
		foreach (var id in new[] { "fresh", "old", "quiet" })
			_client.Sessions[id] = FakeSession.WithEntries(2, "Session " + id);

		var locator = new ScriptedLocator();
		locator.Results.Enqueue(new DaemonDiscoveryResult(DaemonDiscoveryStatus.Found, new DaemonEndpoint(new Uri("http://127.0.0.1:1/tdcp"), "t", "r", 1)));
		var connection = new ConnectionViewModel(locator, new FixedClientFactory(_client), _ui, NullLogger<ConnectionViewModel>.Instance, TimeSpan.FromMilliseconds(5));
		_main = new MainViewModel(
			connection,
			c => new TreeViewModel(c, _ui, NullLogger<TreeViewModel>.Instance, TimeSpan.Zero),
			c => new SessionPaneViewModel(c, new FakeTranscriptSurface(), new MarkdigEntryRenderer(new UrlPolicy()), new FakeUrlLauncher(), _ui,
				new SwitchLatencyTracker(NullLogger<SwitchLatencyTracker>.Instance), NullLogger<SessionPaneViewModel>.Instance),
			new ChromeMemoryState(),
			NullLogger<MainViewModel>.Instance);
		_display = new ListDisplayService(_store, _ui);
		_display.Start();
		_host = new SessionListsHost(connection, _main, _ui, NullLoggerFactory.Instance, display: _display);
		_ = _main.StartAsync(CancellationToken.None);
	}

	public void Dispose()
	{
		_host.Recent.Dispose();
		_host.Dispose();
	}

	private sealed class ChromeMemoryState : IClientStateStore
	{
		public ClientStateModel State { get; } = new();

		public Task LoadAsync() => Task.CompletedTask;

		public void Update(Action<ClientStateModel> change) => change(State);

		public Task FlushAsync() => Task.CompletedTask;
	}

	private static ToggleButton Layout(RecentDropdown dropdown)
	{
		dropdown.Resources["Colour.RecentTier1"] = Frozen(Tier1);
		dropdown.Resources["Colour.RecentTier4"] = Frozen(Tier4);
		dropdown.Measure(new Size(700, 40));
		dropdown.Arrange(new Rect(0, 0, 700, 40));
		dropdown.UpdateLayout();
		return (ToggleButton)dropdown.FindName("Toggle")!;
	}

	private static SolidColorBrush Frozen(Color color)
	{
		var brush = new SolidColorBrush(color);
		brush.Freeze();
		return brush;
	}

	private static Color? BackgroundOf(ToggleButton toggle) => (toggle.Background as SolidColorBrush)?.Color;

	[Fact]
	public void TheSetting_IsOnByDefault_AndAnOlderFileWithoutTheKeyReadsAsOn()
	{
		Assert.True(new AppearanceSettings().ShowRecencyInRecentDropdown);
		Assert.True(new AppearanceSectionViewModel(_store).ShowRecencyInRecentDropdown);
		Assert.True(System.Text.Json.JsonSerializer.Deserialize<AppearanceSettings>("{\"theme\":\"Light\",\"tagFontSize\":12}", new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!.ShowRecencyInRecentDropdown);
	}

	[Fact]
	public async Task TheSettingsCheckbox_Saves_IsAChange_AndComesBackOnReload()
	{
		var section = new AppearanceSectionViewModel(_store);
		Assert.False(section.HasChanges);

		section.ShowRecencyInRecentDropdown = false;
		Assert.True(section.HasChanges);
		Assert.True(await section.SaveAsync());
		Assert.False(_store.Get<AppearanceSettings>().ShowRecencyInRecentDropdown);
		Assert.False(section.HasChanges);

		var reopened = new AppearanceSectionViewModel(_store);
		Assert.False(reopened.ShowRecencyInRecentDropdown);
	}

	[Fact]
	public void TheClosedButton_TakesTheOpenSessionsRecencyTint_AndOnlyWhileTheSettingIsOn()
	{
		_main.Tree!.SelectNodeById("fresh");
		Assert.Equal(4, _host.Recent.RecencyLevel);

		StaTestRunner.Run(() =>
		{
			var dropdown = new RecentDropdown { DataContext = _host.Recent };
			var toggle = Layout(dropdown);
			Assert.Equal(Tier1, BackgroundOf(toggle));   // level 4 = the newest tier (Colour.RecentTier1)

			_store.Update<AppearanceSettings>(a => a.ShowRecencyInRecentDropdown = false);   // live: no reopening, no restart
			dropdown.UpdateLayout();
			Assert.Equal(0, _host.Recent.RecencyLevel);
			Assert.NotEqual(Tier1, BackgroundOf(toggle));

			_store.Update<AppearanceSettings>(a => a.ShowRecencyInRecentDropdown = true);
			dropdown.UpdateLayout();
			Assert.Equal(Tier1, BackgroundOf(toggle));
		});
	}

	[Fact]
	public void TheTint_FollowsTheOpenSession_AndIsPlainForASessionWithoutRecentActivity()
	{
		StaTestRunner.Run(() =>
		{
			var dropdown = new RecentDropdown { DataContext = _host.Recent };
			var toggle = Layout(dropdown);

			_main.Tree!.SelectNodeById("quiet");   // 3 hours ago: tier 2 of the row colours, which this test does not define
			Assert.Equal(2, _host.Recent.RecencyLevel);

			_main.Tree.SelectNodeById("old");   // three days ago
			dropdown.UpdateLayout();
			Assert.Equal(0, _host.Recent.RecencyLevel);
			Assert.NotEqual(Tier1, BackgroundOf(toggle));

			_main.Tree.SelectNodeById("fresh");
			dropdown.UpdateLayout();
			Assert.Equal(Tier1, BackgroundOf(toggle));

			_main.Session!.Close();
			dropdown.UpdateLayout();
			Assert.Equal(0, _host.Recent.RecencyLevel);
		});
	}

	// ---- FR.9.15 / TC.RD.05, TC.RD.06: hovering keeps the recency colour; the active workers badge of the closed button (FR.1.28, TC.AW.07) ----

	[Fact]
	public void HoverAndOpen_LayAFaintOverlay_AndNeverReplaceTheRecencyBackground()   // TC.RD.05
	{
		_main.Tree!.SelectNodeById("fresh");
		StaTestRunner.Run(() =>
		{
			var dropdown = new RecentDropdown { DataContext = _host.Recent };
			var toggle = Layout(dropdown);
			var template = toggle.Template;
			var chrome = (Border)template.FindName("Chrome", toggle);
			var overlay = (Border)template.FindName("Overlay", toggle);

			Assert.Equal(Tier1, (chrome.Background as SolidColorBrush)?.Color);
			Assert.Equal(Visibility.Collapsed, overlay.Visibility);
			Assert.InRange(overlay.Opacity, 0.01, 0.9);   // faint
			Assert.False(overlay.IsHitTestVisible);

			// No trigger of the template replaces the chrome's background: hover and open only show the overlay.
			var setters = template.Triggers.OfType<Trigger>().SelectMany(t => t.Setters.OfType<Setter>()).ToList();
			Assert.DoesNotContain(setters, st => st.TargetName == "Chrome" && st.Property == Border.BackgroundProperty);
			var hover = template.Triggers.OfType<Trigger>().Single(t => t.Property == UIElement.IsMouseOverProperty);
			Assert.Contains(hover.Setters.OfType<Setter>(), st => st.TargetName == "Overlay" && st.Property == UIElement.VisibilityProperty && Equals(st.Value, Visibility.Visible));

			// Open (checked) shows the overlay and still keeps the tier colour. A button of its own: the dropdown's one is tied to its popup, which does not open without a window.
			var open = new ToggleButton { Style = (Style)dropdown.FindResource("RecentToggle"), Background = Frozen(Tier1), IsChecked = true, Width = 300, Height = 30 };
			open.Measure(new Size(300, 30));
			open.Arrange(new Rect(0, 0, 300, 30));
			open.UpdateLayout();
			Assert.Equal(Visibility.Visible, ((Border)open.Template.FindName("Overlay", open)).Visibility);
			Assert.Equal(Tier1, (((Border)open.Template.FindName("Chrome", open)).Background as SolidColorBrush)?.Color);
			open.IsChecked = false;
			open.UpdateLayout();
			Assert.Equal(Visibility.Collapsed, ((Border)open.Template.FindName("Overlay", open)).Visibility);
		});
	}

	[Fact]
	public void TheOpenListsRows_KeepTheirOwnColour_UnderTheHoverOverlay()   // TC.RD.06
	{
		StaTestRunner.Run(() =>
		{
			var dropdown = new RecentDropdown { DataContext = _host.Recent };
			Layout(dropdown);
			var row = new SessionRowViewModel("fresh", "Fresh one", "Repo", new SessionStateDto("fresh", false, false, null, false, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, false, null));
			var presenter = new ContentPresenter { Content = row, ContentTemplate = (DataTemplate)dropdown.FindResource("DropdownRowTemplate"), Width = 400 };
			presenter.Resources["Colour.RecentTier1"] = Frozen(Tier1);
			presenter.Measure(new Size(400, 60));
			presenter.Arrange(new Rect(0, 0, 400, 60));
			presenter.UpdateLayout();

			var button = Descendants<Button>(presenter).First(b => (string?)b.Tag == "open");
			var template = button.Template;
			var hover = (Border)template.FindName("Hover", button);
			Assert.Equal(Visibility.Collapsed, hover.Visibility);
			Assert.InRange(hover.Opacity, 0.01, 0.9);
			Assert.False(hover.IsHitTestVisible);
			var setters = template.Triggers.OfType<Trigger>().SelectMany(t => t.Setters.OfType<Setter>()).ToList();
			Assert.All(setters, st => Assert.Equal("Hover", st.TargetName));   // nothing but the overlay changes on hover
			var tint = Descendants<Border>(presenter).First(b => b.Background is SolidColorBrush { Color: var c } && c == Tier1);
			Assert.NotNull(tint);   // the row's own recency colour sits under the overlay
		});
	}

	[Fact]
	public void TheClosedButton_ShowsTheOpenSessionsActiveWorkers_LiveAndHiddenAtZero()   // TC.AW.07
	{
		_main.Tree!.SelectNodeById("fresh");
		StaTestRunner.Run(() =>
		{
			var dropdown = new RecentDropdown { DataContext = _host.Recent };
			Layout(dropdown);
			var badge = (Border)dropdown.FindName("WorkersBadge")!;
			Assert.Equal(Visibility.Collapsed, badge.Visibility);

			var now = DateTimeOffset.UtcNow;
			_client.RaiseNotification("session.changed", new SessionChangedPayload(new SessionStateDto("fresh", false, false, null, false, now, now, false, null, RunningWorkerCount: 3)));
			dropdown.UpdateLayout();
			Assert.Equal(3, _host.Recent.RunningWorkerCount);
			Assert.Equal(Visibility.Visible, badge.Visibility);
			Assert.Equal("3", ((TextBlock)badge.Child).Text);

			_client.RaiseNotification("session.changed", new SessionChangedPayload(new SessionStateDto("fresh", false, false, null, false, now, now, false, null)));
			dropdown.UpdateLayout();
			Assert.Equal(0, _host.Recent.RunningWorkerCount);
			Assert.Equal(Visibility.Collapsed, badge.Visibility);
		});
	}

	// ---- FR.1.33 / UC.RD.03 / TC.RD.07 to TC.RD.11: the profile name on the closed button ----

	private void Changed(string id, string? profile, string? pinned = null, string? switching = null)
	{
		var now = DateTimeOffset.UtcNow;
		_client.RaiseNotification("session.changed", new SessionChangedPayload(new SessionStateDto(id, false, false, null, false, now, now, false, profile, PinnedProfileName: pinned, SwitchingToProfileName: switching)));
	}

	private static TextBlock ProfileOf(RecentDropdown dropdown) => (TextBlock)dropdown.FindName("ProfileLabel")!;

	private static TextBlock SwitchingOf(RecentDropdown dropdown) => (TextBlock)dropdown.FindName("SwitchingLabel")!;

	private static TextBlock NameOf(RecentDropdown dropdown) => (TextBlock)dropdown.FindName("Label")!;

	private static void LayoutAt(RecentDropdown dropdown, double width)
	{
		dropdown.Measure(new Size(width, 40));
		dropdown.Arrange(new Rect(0, 0, width, 40));
		dropdown.UpdateLayout();
	}

	[Fact]
	public void TheClosedButton_ShowsTheProfileBetweenTheNameAndTheBadge_PinnedElseProfile_HiddenWithoutOne()   // TC.RD.07, TC.RD.08
	{
		_main.Tree!.SelectNodeById("fresh");
		StaTestRunner.Run(() =>
		{
			var dropdown = new RecentDropdown { DataContext = _host.Recent };
			Layout(dropdown);
			var badge = (Border)dropdown.FindName("WorkersBadge")!;
			Assert.Equal(Visibility.Collapsed, ProfileOf(dropdown).Visibility);   // no profile on the open session

			Changed("fresh", "Work");
			dropdown.UpdateLayout();
			var profile = ProfileOf(dropdown);
			Assert.Equal(Visibility.Visible, profile.Visibility);
			Assert.Equal("Work", profile.Text);
			Assert.Equal("Work", profile.ToolTip);
			Assert.Equal(10, profile.FontSize);   // the row's secondary style (SessionRowFace)
			Assert.True(profile.Opacity < 1);

			Changed("fresh", "Work", pinned: "Personal");   // the pinned profile wins, as in the row
			dropdown.UpdateLayout();
			Assert.Equal("Personal", profile.Text);

			// from the left: the name, the profile, the badge
			var now = DateTimeOffset.UtcNow;
			_client.RaiseNotification("session.changed", new SessionChangedPayload(new SessionStateDto("fresh", false, false, null, false, now, now, false, "Work", PinnedProfileName: "Personal", RunningWorkerCount: 2)));
			dropdown.UpdateLayout();
			Assert.Equal(Visibility.Visible, badge.Visibility);
			Assert.True(profile.TranslatePoint(default, dropdown).X > NameOf(dropdown).TranslatePoint(default, dropdown).X);
			Assert.True(badge.TranslatePoint(default, dropdown).X > profile.TranslatePoint(default, dropdown).X);

			_main.Session!.Close();   // no session: no profile
			dropdown.UpdateLayout();
			Assert.Equal(Visibility.Collapsed, profile.Visibility);
			Assert.Equal(string.Empty, _host.Recent.CurrentProfileName);
		});
	}

	[Fact]
	public void AWaitingSwitch_ShowsTheCautionaryArrowAndTarget_AndLeavesWhenTheSwitchIsDone()   // TC.RD.09
	{
		_main.Tree!.SelectNodeById("fresh");
		StaTestRunner.Run(() =>
		{
			var dropdown = new RecentDropdown { DataContext = _host.Recent };
			Layout(dropdown);
			Changed("fresh", "Work");
			dropdown.UpdateLayout();
			Assert.Equal(Visibility.Collapsed, SwitchingOf(dropdown).Visibility);

			Changed("fresh", "Work", switching: "Personal");
			dropdown.UpdateLayout();
			var target = SwitchingOf(dropdown);
			Assert.Equal(Visibility.Visible, target.Visibility);
			Assert.Equal("→ Personal", target.Text);
			Assert.Equal("Work", ProfileOf(dropdown).Text);   // the current profile stays
			Assert.True(target.TranslatePoint(default, dropdown).X > ProfileOf(dropdown).TranslatePoint(default, dropdown).X);

			Changed("fresh", "Personal");
			dropdown.UpdateLayout();
			Assert.Equal(Visibility.Collapsed, SwitchingOf(dropdown).Visibility);
			Assert.Equal("Personal", ProfileOf(dropdown).Text);
		});
	}

	[Fact]
	public void InANarrowTitleBar_TheNameTrimsFirst_AndTheProfileKeepsItsWidthUpTo150()   // TC.RD.10
	{
		_main.Tree!.SelectNodeById("fresh");
		StaTestRunner.Run(() =>
		{
			var dropdown = new RecentDropdown { DataContext = _host.Recent };
			Changed("fresh", "Work profile");
			LayoutAt(dropdown, 700);
			var profile = ProfileOf(dropdown);
			var name = NameOf(dropdown);
			var wideProfile = profile.ActualWidth;
			var wideName = name.ActualWidth;
			Assert.True(wideProfile > 0);

			LayoutAt(dropdown, wideProfile + 110);
			Assert.Equal(wideProfile, profile.ActualWidth, 0.5);   // the profile keeps its width
			Assert.True(name.ActualWidth < wideName, "the name trims");

			Changed("fresh", new string('P', 80));   // a very long profile: 150 px at most
			LayoutAt(dropdown, 700);
			Assert.InRange(profile.ActualWidth, 1, 150.5);
			LayoutAt(dropdown, 260);
			Assert.InRange(profile.ActualWidth, 1, 150.5);
			Assert.Equal(new string('P', 80), profile.ToolTip);   // the full name is the tooltip
		});
	}

	/// <summary>FR.1.33 item 5 at every width: the badge and the chevron keep their size and their distance from the right edge; the name trims first, then the target, then the profile.</summary>
	[Theory]
	[InlineData(600)]
	[InlineData(330)]
	[InlineData(250)]
	[InlineData(180)]
	[InlineData(120)]
	public void AtNarrowWidths_BadgeAndChevronStay_AndTheNameTrimsBeforeTheTargetBeforeTheProfile(double width)   // TC.RD.10
	{
		_main.Tree!.SelectNodeById("fresh");
		StaTestRunner.Run(() =>
		{
			var dropdown = new RecentDropdown { DataContext = _host.Recent };
			var now = DateTimeOffset.UtcNow;
			var profileText = new string('P', 80);
			_client.RaiseNotification("session.changed", new SessionChangedPayload(new SessionStateDto("fresh", false, false, null, false, now, now, false, profileText, SwitchingToProfileName: new string('Q', 12), RunningWorkerCount: 4)));
			var badge = (Border)dropdown.FindName("WorkersBadge")!;
			var chevron = (TextBlock)dropdown.FindName("Chevron")!;
			var profile = ProfileOf(dropdown);
			var target = SwitchingOf(dropdown);
			var name = NameOf(dropdown);

			LayoutAt(dropdown, 700);   // the reference: nothing is trimmed
			var badgeWidth = badge.ActualWidth;
			var chevronWidth = chevron.ActualWidth;
			var fullName = name.ActualWidth;
			var fullProfileSlot = System.Windows.Controls.Primitives.LayoutInformation.GetLayoutSlot(profile).Width;
			var fullTargetSlot = System.Windows.Controls.Primitives.LayoutInformation.GetLayoutSlot(target).Width;
			var badgeFromRight = 700 - (badge.TranslatePoint(default, dropdown).X + badge.ActualWidth);
			var chevronFromRight = 700 - (chevron.TranslatePoint(default, dropdown).X + chevron.ActualWidth);
			Assert.True(badgeWidth > 0 && chevronWidth > 0);

			LayoutAt(dropdown, width);
			Assert.Equal(badgeWidth, badge.ActualWidth, 0.5);
			Assert.Equal(chevronWidth, chevron.ActualWidth, 0.5);
			Assert.Equal(badgeFromRight, width - (badge.TranslatePoint(default, dropdown).X + badge.ActualWidth), 0.5);
			Assert.Equal(chevronFromRight, width - (chevron.TranslatePoint(default, dropdown).X + chevron.ActualWidth), 0.5);
			Assert.True(badge.TranslatePoint(default, dropdown).X >= 0);

			// A trimmed text still reports the width of its ellipsis but is clipped to its layout slot, so the slots tell what each one was given.
			double Slot(FrameworkElement e) => System.Windows.Controls.Primitives.LayoutInformation.GetLayoutSlot(e).Width;
			if (Slot(profile) < fullProfileSlot - 0.5)
				Assert.True(Slot(target) <= target.Margin.Left + 0.5 && Slot(name) <= 0.5, $"the profile trims only after the target and the name are gone: name={Slot(name)} target={Slot(target)} profile={Slot(profile)}/{fullProfileSlot}");
			if (Slot(target) < fullTargetSlot - 0.5)
				Assert.True(Slot(name) <= 0.5, $"the target trims only after the name is gone: name={Slot(name)} target={Slot(target)}/{fullTargetSlot}");
			if (width >= 330)
				Assert.True(Slot(profile) >= fullProfileSlot - 0.5, "the profile is whole down to 330 px");   // the name and the target go first
			if (width >= 600)
				Assert.True(name.ActualWidth >= fullName - 0.5 && Slot(target) >= fullTargetSlot - 0.5, "nothing is trimmed at 600 px");
		});
	}

	[Fact]
	public void TheProfileText_HasAnAutomationName()   // FR.1.33 item 2
	{
		_main.Tree!.SelectNodeById("fresh");
		StaTestRunner.Run(() =>
		{
			var dropdown = new RecentDropdown { DataContext = _host.Recent };
			Changed("fresh", "Work profile");
			LayoutAt(dropdown, 600);
			Assert.Equal("Work profile", System.Windows.Automation.AutomationProperties.GetName(ProfileOf(dropdown)));
		});
	}

	[Fact]
	public void TheBadgeAndTheChevron_KeepTheirPlaces_WhateverTheProfile()   // TC.RD.10
	{
		_main.Tree!.SelectNodeById("fresh");
		StaTestRunner.Run(() =>
		{
			var dropdown = new RecentDropdown { DataContext = _host.Recent };
			var now = DateTimeOffset.UtcNow;
			_client.RaiseNotification("session.changed", new SessionChangedPayload(new SessionStateDto("fresh", false, false, null, false, now, now, false, null, RunningWorkerCount: 4)));
			LayoutAt(dropdown, 440);
			var badge = (Border)dropdown.FindName("WorkersBadge")!;
			var before = badge.TranslatePoint(default, dropdown).X + badge.ActualWidth;

			_client.RaiseNotification("session.changed", new SessionChangedPayload(new SessionStateDto("fresh", false, false, null, false, now, now, false, new string('P', 80), SwitchingToProfileName: new string('Q', 40), RunningWorkerCount: 4)));
			LayoutAt(dropdown, 440);
			var after = badge.TranslatePoint(default, dropdown).X + badge.ActualWidth;

			Assert.Equal(Visibility.Visible, badge.Visibility);
			Assert.Equal(before, after, 0.5);   // the chevron's left edge stays: the badge sits right before it
			Assert.True(badge.TranslatePoint(default, dropdown).X + badge.ActualWidth <= dropdown.ActualWidth);
		});
	}

	[Fact]
	public void TheProfile_FollowsTheOpenSession_AndItsLiveChanges()   // TC.RD.11
	{
		_main.Tree!.SelectNodeById("fresh");
		StaTestRunner.Run(() =>
		{
			var dropdown = new RecentDropdown { DataContext = _host.Recent };
			Layout(dropdown);
			Changed("fresh", "Work");
			Changed("quiet", "Home");
			dropdown.UpdateLayout();
			Assert.Equal("Work", ProfileOf(dropdown).Text);

			_main.Tree.SelectNodeById("quiet");   // another session opens: its profile
			dropdown.UpdateLayout();
			Assert.Equal("Home", ProfileOf(dropdown).Text);

			Changed("quiet", "Home", pinned: "Work");   // a live change of the open session
			dropdown.UpdateLayout();
			Assert.Equal("Work", ProfileOf(dropdown).Text);

			Changed("fresh", "Elsewhere");   // another session changing does not touch the button
			dropdown.UpdateLayout();
			Assert.Equal("Work", ProfileOf(dropdown).Text);
		});
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

	[Fact]
	public void TheScreensaverPicture_FillsTheScreen_KeepingTheAspectRatioAndCropping()
	{
		StaTestRunner.Run(() =>
		{
			var overlay = new ScreensaverOverlay();
			var picture = (Image)overlay.FindName("Picture")!;
			Assert.Equal(Stretch.UniformToFill, picture.Stretch);
		});
	}
}
