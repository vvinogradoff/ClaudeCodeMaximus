using System.Windows;
using System.Windows.Controls;
using AvalonDock.Layout;
using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.WPF.Infrastructure;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.State;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.ViewModels;
using TessynDesktop.WPF.Views;

namespace TessynDesktop.WPF.Tests;

/// <summary>
/// FR.6.11, FR.6.16, FR.1.16 (stage 1 integration): the hooks between the layout core, the panels' views and the chrome: a selected or brought-forward tab tells its content
/// (<see cref="IPanelSelectionAware"/>), and the Focus tab's header is the badge header on the Focus view model.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class ShellIntegrationTests
{
	private sealed class AwarePanel : Border, IPanelSelectionAware
	{
		public int Selected { get; private set; }

		public void OnPanelSelected() => Selected++;
	}

	private static (DockingArea Area, Window Window, AwarePanel Recent, AwarePanel Sessions) Build()
	{
		var recent = new AwarePanel();
		var sessions = new AwarePanel();
		var state = new MemoryClientState();
		var area = new DockingArea(new Border { MinWidth = 100 });
		var window = new Window { Content = area, Width = 1400, Height = 800, Left = -3000, Top = -3000, ShowActivated = false, ShowInTaskbar = false };
		List<ShellPanelDefinition> panels =
		[
			new() { Id = Constants.DockedShell.SessionsPanelId, Title = "Sessions", Content = sessions, IsLeft = true },
			new() { Id = Constants.DockedShell.RecentPanelId, Title = "Recent", Content = recent, IsLeft = true },
			new() { Id = Constants.DockedShell.TagsPanelId, Title = "Tags", Content = new Border(), IsLeft = true },
			new() { Id = Constants.DockedShell.FocusPanelId, Title = "Focus", Content = new Border(), IsLeft = false },
		];
		var memory = new ShellLayoutMemory(new ClientSettingsStore(state, NullLogger<ClientSettingsStore>.Instance), state, false);
		area.BuildShell(panels, memory, new CapturingLogger<DockingArea>(), () => ([new MonitorBounds(0, 0, 1920, 1080)], new MonitorBounds(0, 0, 1920, 1080)));
		window.Show();
		window.UpdateLayout();
		return (area, window, recent, sessions);
	}

	[Fact]
	public void SelectingATab_AndBringingAPanelForward_TellTheContent()
	{
		StaTestRunner.Run(() =>
		{
			var (area, window, recent, sessions) = Build();
			try
			{
				Assert.Equal(0, recent.Selected);
				area.FindPanel(Constants.DockedShell.RecentPanelId)!.IsSelected = true;   // the user picks the tab
				Assert.Equal(1, recent.Selected);

				var before = sessions.Selected;
				area.BringForward(Constants.DockedShell.SessionsPanelId);   // brought forward: another tab of the pane comes up
				Assert.True(sessions.Selected > before);

				before = sessions.Selected;
				area.BringForward(Constants.DockedShell.SessionsPanelId);   // already selected: still told (the Recent collapse rule must run)
				Assert.True(sessions.Selected > before);
			}
			finally
			{
				window.Close();
			}
		});
	}

	[Fact]
	public void TheFocusTabHeader_IsTheBadgeHeader_OnTheFocusContentsDataContext_OtherTabsArePlain()
	{
		StaTestRunner.Run(() =>
		{
			var selector = new ShellPanelHeaderTemplateSelector();
			var focusView = new Border { DataContext = new object() };
			var focusPanel = new LayoutAnchorable { ContentId = Constants.DockedShell.FocusPanelId, Title = "Focus", Content = focusView };
			var plainPanel = new LayoutAnchorable { ContentId = Constants.DockedShell.TagsPanelId, Title = "Tags", Content = new Border() };

			var focusHeader = (FocusTabHeader)selector.SelectTemplate(focusPanel, new ContentPresenter()).LoadContent();
			var plainHeader = selector.SelectTemplate(plainPanel, new ContentPresenter()).LoadContent();

			Assert.IsType<FocusTabHeader>(focusHeader);
			Assert.IsType<TextBlock>(plainHeader);
			Assert.NotEqual(selector.SelectTemplate(focusPanel, new ContentPresenter()), selector.SelectTemplate(plainPanel, new ContentPresenter()));
		});
	}
}
