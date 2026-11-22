using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Infrastructure;
using TessynDesktop.WPF.Rendering;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.ViewModels;
using TessynDesktop.WPF.ViewModels.Settings;
using TessynDesktop.WPF.Views;
using TessynDesktop.WPF.Views.Settings;

namespace TessynDesktop.WPF.Tests;

/// <summary>
/// FR.33 as real controls on an STA thread: the model picker's headers and hint cannot be picked, a Codex session hides the Streaming toggle, an exhausted
/// window takes the limits-error colour, and the Codex and ChatGPT parts of Settings bind without errors and keep text stretching beside fixed buttons (NR.7).
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class CodexViewTests
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

	private static List<T> FindAll<T>(DependencyObject root) where T : DependencyObject
	{
		var found = new List<T>();
		for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
		{
			var child = VisualTreeHelper.GetChild(root, i);
			if (child is T match)
				found.Add(match);

			found.AddRange(FindAll<T>(child));
		}

		return found;
	}

	private static T Named<T>(DependencyObject root, string automationName) where T : FrameworkElement =>
		FindAll<T>(root).First(e => AutomationProperties.GetName(e) == automationName);

	private static bool IsShown(DependencyObject element)
	{
		for (var current = element; current != null; current = VisualTreeHelper.GetParent(current))
			if (current is UIElement { Visibility: not Visibility.Visible })
				return false;

		return true;
	}

	private static void Layout(FrameworkElement element, double width = 800, double height = 900)
	{
		element.Measure(new Size(width, height));
		element.Arrange(new Rect(0, 0, width, height));
		element.UpdateLayout();
	}

	private static SessionPaneViewModel OpenedPane(FakeTessynClient client)
	{
		client.Sessions["g"] = FakeSession.WithEntries(2, "G");
		var pane = new SessionPaneViewModel(client, new FakeTranscriptSurface(), new MarkdigEntryRenderer(new UrlPolicy()), new FakeUrlLauncher(), new ImmediateUiDispatcher(),
			new SwitchLatencyTracker(NullLogger<SwitchLatencyTracker>.Instance), NullLogger<SessionPaneViewModel>.Instance);
		pane.OpenAsync("g").GetAwaiter().GetResult();
		pane.LoadCatalogAsync().GetAwaiter().GetResult();
		return pane;
	}

	[Fact]
	public void ModelPicker_HeadersAndTheHintAreDisabledLines_AndOptionsCarryTheirTooltip()
	{
		WithoutBindingErrors(() =>
		{
			var client = new FakeTessynClient { Catalog = CodexCatalogs.FixtureC() };
			var pane = OpenedPane(client);
			var bar = new CommandBar { DataContext = pane };
			Layout(bar, 900, 100);
			var models = FindAll<ComboBox>(bar)[0];
			Assert.Same(pane.Models, models.ItemsSource);

			// The drop-down's own containers need a shown window; the container style the picker uses is applied to stand-ins instead.
			var containers = pane.Models.Select(m =>
			{
				var item = new ComboBoxItem { DataContext = m, Content = m, Style = models.ItemContainerStyle };
				Layout(item, 300, 30);
				return (Option: m, Item: item);
			}).ToList();
			Assert.All(containers, c => Assert.Equal(c.Option.IsSelectable, c.Item.IsEnabled));
			Assert.Equal(3, containers.Count(c => c.Option.IsHeader && !c.Item.IsEnabled));
			Assert.Equal("GPT (ChatGPT plan)", AutomationProperties.GetName(containers.Single(c => c.Option.Id == "header:GPT (ChatGPT plan)").Item));
			Assert.Equal("GPT-X Astra" + Environment.NewLine + "Codex suggests gpt-x-astra-2", containers.Single(c => c.Option.Id == "gpt-x-astra").Option.ToolTip);
		});
	}

	[Fact]
	public void ACodexSession_HidesTheStreamingToggle()
	{
		WithoutBindingErrors(() =>
		{
			var client = new FakeTessynClient { Catalog = CodexCatalogs.FixtureC() };
			var pane = OpenedPane(client);
			var toggles = new CommandToggles { DataContext = pane };
			Layout(toggles, 400, 60);
			var streaming = Named<ToggleButton>(toggles, "Streaming Session Mode");
			Assert.Equal(Visibility.Visible, streaming.Visibility);

			pane.SelectedModel = pane.Models.Single(m => m.Id == "gpt-x-sol");
			toggles.UpdateLayout();

			Assert.Equal(Visibility.Collapsed, streaming.Visibility);
			Assert.True(IsShown(Named<ToggleButton>(toggles, "Focus item sound")));
		});
	}

	[Fact]
	public async Task TheGeneralTab_ShowsTheCodexSection_WithTheStatusLine_AndThePathStretchesBesideBrowse()
	{
		var client = new FakeTessynClient().AnnouncingSettings().WithSettingsValues();
		var values = (GeneralSettingsDto)client.SettingsValues[RemoteProtocol.SettingsApi.GeneralSection];
		client.SettingsValues[RemoteProtocol.SettingsApi.GeneralSection] = values with
		{
			CodexStatus = new CodexStatusDto(true, @"C:\Tools\codex.exe", "0.171.0", "0.171.x", null, @"Codex 0.171.0 found at C:\Tools\codex.exe"),
			CodexModelTiers = [new("astra", 3), new("gpt", 2)],
		};
		var general = new GeneralSectionViewModel(client, NullLogger<GeneralSectionViewModel>.Instance);
		await general.InitializeAsync(RemoteProtocol.SettingsApi.Capability);

		WithoutBindingErrors(() =>
		{
			var view = new GeneralSettingsView { DataContext = general };
			Layout(view);

			var status = (TextBlock)view.FindName("CodexStatusLine");
			Assert.True(IsShown(status));
			Assert.Equal(@"Codex 0.171.0 found at C:\Tools\codex.exe", status.Text);
			Assert.Equal(status.Text, AutomationProperties.GetName(status));   // UI Automation reads the line itself
			Assert.Equal(2, FindAll<TextBox>(view).Count(t => AutomationProperties.GetName(t) == "Tier match"));

			var path = Named<TextBox>(view, "Codex path");
			var browse = Named<Button>(view, "Browse for Codex");
			var narrowPath = path.ActualWidth;
			var narrowBrowse = browse.ActualWidth;
			Layout(view, 1400);
			Assert.True(path.ActualWidth >= narrowPath);
			Assert.Equal(narrowBrowse, browse.ActualWidth);
			Assert.True(browse.TranslatePoint(new Point(), view).X > path.TranslatePoint(new Point(), view).X);
		});
	}

	[Fact]
	public async Task TheProfilesTab_ShowsTheChatGptGroup_AndTheCodeSignIn()
	{
		var client = new FakeTessynClient().AnnouncingSettings();
		client.ServerInfo = client.ServerInfo! with { Capabilities = [.. client.ServerInfo.Capabilities, RemoteProtocol.CodexProfilesAdmin.Capability] };
		client.Profiles = [new("default", "[Sys] me@example.com", true, "me@example.com", ProfileAuthState.Unknown, true)];
		client.CodexProfiles = [new("codex-default", "ChatGPT · me@example.com", "me@example.com", "plus", true, true, false, false)];
		var presenter = new FakeDialogPresenter
		{
			Answer = d =>
			{
				d.ChooseCommand.Execute(d.Choices.Single(c => c.Id == Constants.Codex.CodeChoiceId));
				return Task.CompletedTask;
			},
		};
		var ui = new ImmediateUiDispatcher();
		var profiles = new ProfileViewModel(new DialogService(presenter, ui), ui, NullLogger<ProfileViewModel>.Instance, new FakeSystemUrlLauncher(), new FakeClipboardWriter());
		await profiles.AttachAsync(client);
		await profiles.NewChatGptProfileAsync();
		var section = new ModelsProfilesSectionViewModel(client, profiles, NullLogger<ModelsProfilesSectionViewModel>.Instance);

		WithoutBindingErrors(() =>
		{
			var view = new ModelsProfilesSettingsView { DataContext = section };
			Layout(view);

			Assert.True(IsShown(Named<StackPanel>(view, "ChatGPT profiles")));
			Assert.True(IsShown(FindAll<TextBlock>(view).Single(t => t.Text == Constants.Codex.CodexDefaultWarning)));
			Assert.False(IsShown(Named<Button>(view, "Delete: ChatGPT · me@example.com (codex-default)")));   // Codex Default is never deleted
			Assert.True(IsShown(Named<Border>(view, "ChatGPT sign-in")));
			Assert.Equal("ABCD-1234", Named<TextBox>(view, "Sign-in code").Text);
			Assert.True(IsShown(FindAll<TextBlock>(view).Single(t => t.Text == Constants.Codex.DeviceCodeNote)));
			Assert.True(IsShown(Named<Button>(view, "Cancel sign-in")));
		});
	}

	[Fact]
	public void AnExhaustedWindow_TakesTheLimitsErrorColour()
	{
		StaTestRunner.Run(() =>
		{
			var view = new StatusBarItems();
			var limit = new SolidColorBrush(Colors.Gold);
			view.Resources["Colour.LimitWarning"] = limit;
			var vm = new ExhaustedFace();
			view.DataContext = vm;
			Layout(view, 600, 30);

			Assert.Same(limit, ((ProgressBar)view.FindName("FiveHourMeter")).Foreground);
			Assert.NotSame(limit, ((ProgressBar)view.FindName("SevenDayMeter")).Foreground);
			Assert.Equal(Visibility.Collapsed, ((FrameworkElement)VisualTreeHelper.GetParent((ProgressBar)view.FindName("SevenDayMeter"))).Visibility);
		});
	}

	/// <summary>The status bar members the view binds to, with the top window out of credits and no bottom window.</summary>
	public sealed class ExhaustedFace
	{
		public string ModelText => "GPT-X Sol  ·  ChatGPT plan";
		public string UsageNote => string.Empty;
		public bool HasNote => false;
		public bool HasUsage => true;
		public bool HasFiveHour => true;
		public bool HasSevenDay => false;
		public double FiveHourPercent => 100;
		public double SevenDayPercent => 0;
		public string FiveHourLabel => "5h: 100%";
		public string SevenDayLabel => string.Empty;
		public bool IsFiveHourExhausted => true;
		public bool IsSevenDayExhausted => false;
		public string? FiveHourToolTip => "ChatGPT plan limit reached";
		public string? SevenDayToolTip => null;
	}
}
