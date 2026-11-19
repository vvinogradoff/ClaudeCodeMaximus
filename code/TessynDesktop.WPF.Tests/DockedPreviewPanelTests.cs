using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using AvalonDock.Layout;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.WPF.Infrastructure;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.State;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.ViewModels;
using TessynDesktop.WPF.Views;

namespace TessynDesktop.WPF.Tests;

/// <summary>FR.29.17–FR.29.21 (stage 1): a docked preview panel's handle, factory and content on a model-only docking surface and an STA thread.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class DockedPreviewPanelTests
{
	private const string GroupId = "text-code";

	private static readonly PreviewGroupModel Group = new() { Id = GroupId, Name = "Text and code", Patterns = "md txt" };

	private sealed class Fixture : IDisposable
	{
		public Fixture(bool pinned = false, PreviewDockLayoutModel? remembered = null, bool defaultPanel = false, PreviewDockLayoutModel? own = null)
		{
			Settings = new FakePreviewSettingsService(Group);
			if (remembered != null)
				Settings.DockLayouts[defaultPanel ? Constants.Previews.DefaultPanelId : GroupId] = remembered;

			Surface = new TestDockingSurface();
			Content = new FakePreviewContentFactory();
			Logger = new CapturingLogger<DockedPreviewPanelHandle>();
			var services = new ServiceCollection();
			services.AddSingleton<IPreviewSettingsService>(Settings);
			services.AddSingleton<IPreviewContentFactory>(Content);
			services.AddSingleton<IUiDispatcher>(new ImmediateUiDispatcher());
			services.AddSingleton<ILoggerFactory>(new CapturingLoggerFactory(Logger));
			var host = new DockingSurfaceHost();
			host.Register(Surface);
			services.AddSingleton(host);
			Provider = services.BuildServiceProvider();
			Factory = new PreviewDockFactory(Provider);
			Handle = (DockedPreviewPanelHandle)Factory.Create(defaultPanel ? null : Group, pinned, own);
			Client = new FakeTessynClient();
			Client.Files["h"] = ("note.md", "text/markdown", "# hi"u8.ToArray());
		}

		public FakePreviewSettingsService Settings { get; }

		public TestDockingSurface Surface { get; }

		public FakePreviewContentFactory Content { get; }

		public CapturingLogger<DockedPreviewPanelHandle> Logger { get; }

		public ServiceProvider Provider { get; }

		public PreviewDockFactory Factory { get; }

		public DockedPreviewPanelHandle Handle { get; }

		public FakeTessynClient Client { get; }

		public PreviewItemModel Item(string name = "note.md") => new() { Kind = PreviewItemKind.File, FileName = name };

		public void ShowFile() => Handle.Show(Item(), new PreviewTarget("h", "note.md"), Client);

		public void Dispose()
		{
			Provider.Dispose();
			Content.Dispose();
		}
	}

	private sealed class CapturingLoggerFactory(ILogger logger) : ILoggerFactory
	{
		public void AddProvider(ILoggerProvider provider)
		{
		}

		public ILogger CreateLogger(string categoryName) => logger;

		public void Dispose()
		{
		}
	}

	private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
	{
		var count = VisualTreeHelper.GetChildrenCount(root);
		for (var i = 0; i < count; i++)
		{
			var child = VisualTreeHelper.GetChild(root, i);
			if (child is T match)
				yield return match;

			foreach (var nested in Descendants<T>(child))
				yield return nested;
		}
	}

	[Fact]
	public void Create_BuildsAPanelOfTheGroup_NotInTheLayoutYet_AndShowOpensItAtTheFirstTimeLayout()
	{
		StaTestRunner.Run(() =>
		{
			using var f = new Fixture();
			Assert.Null(f.Handle.Panel.Root);
			Assert.Equal(GroupId, f.Handle.GroupId);
			Assert.Equal(GroupId, f.Handle.Panel.ContentId);
			Assert.Null(f.Handle.CurrentPlacement);
			Assert.Null(f.Handle.CurrentLayout);

			f.ShowFile();

			var pane = Assert.IsType<LayoutAnchorablePane>(f.Handle.Panel.Parent);
			Assert.Equal(400, pane.DockWidth.Value);   // FR.29.21: the first time, docked right at 40 % of the area (decision 6)
			Assert.Equal(PreviewDockSide.Right, f.Handle.CurrentLayout!.Side);
			Assert.True(f.Handle.Panel.IsSelected);
			Assert.False(f.Handle.Panel.IsActive);   // FR.29.17: shown without taking the keyboard focus
			Assert.Equal("note.md - Text and code", f.Handle.Panel.Title);
			Assert.Contains(f.Logger.Messages, m => m == "Preview panel opened: group text-code");
		});
	}

	[Fact]
	public void AGroupPanel_OpensAtTheGroupsRememberedLayout()
	{
		StaTestRunner.Run(() =>
		{
			using var f = new Fixture(remembered: new PreviewDockLayoutModel { Side = PreviewDockSide.Bottom, Size = 222 });

			f.ShowFile();

			var read = f.Handle.CurrentLayout!;
			Assert.Equal(PreviewDockSide.Bottom, read.Side);
			Assert.Equal(222, read.Size);
		});
	}

	[Fact]
	public void ANewItem_ReplacesTheContent_AndBringsATabbedBehindPanelForward_WithoutReDocking()
	{
		StaTestRunner.Run(() =>
		{
			using var f = new Fixture();
			f.ShowFile();
			var pane = f.Handle.Panel.Parent;
			f.Handle.Panel.IsSelected = false;

			f.Handle.Show(f.Item("other.md"), new PreviewTarget("h", "other.md"), f.Client);

			Assert.Same(pane, f.Handle.Panel.Parent);
			Assert.True(f.Handle.Panel.IsSelected);
			Assert.Equal("other.md", f.Handle.Item!.FileName);
			Assert.Equal("other.md - Text and code", f.Handle.Panel.Title);
		});
	}

	[Fact]
	public void TheRoutersClose_RemovesThePanel_RaisesClosedNotByUser_AndDisposesTheContent()
	{
		StaTestRunner.Run(() =>
		{
			using var f = new Fixture();
			f.ShowFile();
			PreviewWindowClosedEventArgs? closed = null;
			f.Handle.Closed += (_, e) => closed = e;

			f.Handle.Close();

			Assert.NotNull(closed);
			Assert.False(closed!.ByUser);
			Assert.Null(f.Handle.Panel.Root);
			Assert.All(f.Content.FileSurfaces, s => Assert.True(s.Disposed));
			Assert.Contains(f.Logger.Messages, m => m.StartsWith("Preview panel closed: group text-code", StringComparison.Ordinal));
			f.Handle.Close();   // closing twice is harmless
		});
	}

	[Fact]
	public void TheRoutersClose_SavesTheLayoutFirst_ShownStillTrue()
	{
		StaTestRunner.Run(() =>
		{
			using var f = new Fixture(remembered: new PreviewDockLayoutModel { Side = PreviewDockSide.Left, Size = 260 });
			f.ShowFile();

			f.Handle.Close();

			var saved = f.Settings.GetDockLayout(GroupId);
			Assert.Equal(PreviewDockSide.Left, saved!.Side);
			Assert.True(saved.Shown);
		});
	}

	[Fact]
	public void TheUsersClose_RaisesClosedByUser_AndRemembersTheGroupsLayoutAsClosed()
	{
		StaTestRunner.Run(() =>
		{
			using var f = new Fixture(remembered: new PreviewDockLayoutModel { Side = PreviewDockSide.Left, Size = 260 });
			f.ShowFile();
			PreviewWindowClosedEventArgs? closed = null;
			f.Handle.Closed += (_, e) => closed = e;

			f.Handle.Panel.Close();   // the tab's X

			Assert.True(closed!.ByUser);
			var saved = f.Settings.GetDockLayout(GroupId)!;
			Assert.False(saved.Shown);
			Assert.Equal(PreviewDockSide.Left, saved.Side);
			Assert.Contains(f.Logger.Messages, m => m == "Preview panel closed: group text-code (by user True)");
		});
	}

	[Fact]
	public void HidingThePanel_IsTheUsersClose_Too()
	{
		StaTestRunner.Run(() =>
		{
			using var f = new Fixture();
			f.ShowFile();
			PreviewWindowClosedEventArgs? closed = null;
			f.Handle.Closed += (_, e) => closed = e;

			f.Handle.Panel.Hide();   // the X of a tool window

			Assert.True(closed!.ByUser);
			Assert.Null(f.Handle.Panel.Root);
			Assert.All(f.Content.FileSurfaces, s => Assert.True(s.Disposed));
		});
	}

	[Fact]
	public void ALayoutChange_IsSaved500MsAfterTheLast_AndOnlyWhenItReallyChanged()
	{
		StaTestRunner.Run(() =>
		{
			var time = new ManualTimeProvider();
			var settings = new FakePreviewSettingsService(Group);
			var surface = new TestDockingSurface();
			using var content = new FakePreviewContentFactory();
			var vm = new PreviewWindowViewModel(GroupId, "Text and code", false, settings, content, new ImmediateUiDispatcher());
			var panelContent = new PreviewPanelContent();
			panelContent.Attach(vm, NullLogger.Instance, true);
			var panel = new LayoutAnchorable { ContentId = GroupId, Content = panelContent };
			var handle = new DockedPreviewPanelHandle(vm, panelContent, panel, surface, settings, null, new ImmediateUiDispatcher(), NullLogger.Instance, time);
			var client = new FakeTessynClient();
			handle.Show(new PreviewItemModel { Kind = PreviewItemKind.File, FileName = "note.md" }, new PreviewTarget("h", "note.md"), client);

			surface.RaiseLayoutChanged();   // nothing changed since it opened
			time.Advance(TimeSpan.FromSeconds(1));
			Assert.Empty(settings.DockLayouts);

			((LayoutAnchorablePane)panel.Parent!).DockWidth = new GridLength(333);   // a splitter drag
			surface.RaiseLayoutChanged();
			time.Advance(TimeSpan.FromMilliseconds(400));
			Assert.Empty(settings.DockLayouts);
			((LayoutAnchorablePane)panel.Parent!).DockWidth = new GridLength(350);
			surface.RaiseLayoutChanged();   // every change restarts the wait
			time.Advance(TimeSpan.FromMilliseconds(400));
			Assert.Empty(settings.DockLayouts);
			time.Advance(TimeSpan.FromMilliseconds(200));

			Assert.Equal(350, settings.DockLayouts[GroupId].Size);
			handle.Close();
		});
	}

	[Fact]
	public void APinnedPanel_NeverWritesTheGroupsLayout_ButTellsTheRouter()
	{
		StaTestRunner.Run(() =>
		{
			var time = new ManualTimeProvider();
			var settings = new FakePreviewSettingsService(Group);
			var surface = new TestDockingSurface();
			using var content = new FakePreviewContentFactory();
			var vm = new PreviewWindowViewModel(GroupId, "Text and code", true, settings, content, new ImmediateUiDispatcher());
			var panelContent = new PreviewPanelContent();
			panelContent.Attach(vm, NullLogger.Instance, true);
			var panel = new LayoutAnchorable { ContentId = GroupId + "#pinned1", Content = panelContent };
			var handle = new DockedPreviewPanelHandle(vm, panelContent, panel, surface, settings, new PreviewDockLayoutModel { Side = PreviewDockSide.Top, Size = 200 }, new ImmediateUiDispatcher(), NullLogger.Instance, time);
			var told = 0;
			handle.PlacementChanged += (_, _) => told++;
			handle.Show(new PreviewItemModel { Kind = PreviewItemKind.File, FileName = "note.md" }, new PreviewTarget("h", "note.md"), new FakeTessynClient());
			Assert.Equal(PreviewDockSide.Top, handle.CurrentLayout!.Side);   // its own layout, not the group's

			((LayoutAnchorablePane)panel.Parent!).DockHeight = new GridLength(260);
			surface.RaiseLayoutChanged();
			time.Advance(TimeSpan.FromSeconds(1));

			Assert.Empty(settings.DockLayouts);
			Assert.Equal(1, told);
			handle.Close();
			Assert.Empty(settings.DockLayouts);   // nor does closing it
		});
	}

	[Fact]
	public void PinningAGroupPanel_Logs_RaisesPinned_AndStopsItWritingTheGroupsLayout()
	{
		StaTestRunner.Run(() =>
		{
			using var f = new Fixture();
			f.ShowFile();
			var pinned = 0;
			f.Handle.Pinned += (_, _) => pinned++;
			Layout((FrameworkElement)f.Handle.Panel.Content);
			var pin = Descendants<ToggleButton>((DependencyObject)f.Handle.Panel.Content).Single(b => AutomationProperties.GetName(b) == "Pin panel Text and code");

			var vm = (PreviewWindowViewModel)((PreviewPanelContent)f.Handle.Panel.Content).DataContext;
			vm.PinChecked = true;   // what the Toggle pattern does

			Assert.Equal(1, pinned);
			Assert.True(f.Handle.IsPinned);
			Assert.Contains(f.Logger.Messages, m => m == "Preview panel pinned: group text-code");
			Assert.Contains("#pinned", f.Handle.Panel.ContentId);   // no longer its group's panel
			Assert.False(pin.IsEnabled || pin.IsChecked != true);
			f.Handle.Close();
			Assert.Empty(f.Settings.DockLayouts);
		});
	}

	[Fact]
	public void TheDefaultPreviewPanel_HasItsIdAndTitle_AndNeitherPinNorAutoClose()
	{
		StaTestRunner.Run(() =>
		{
			using var f = new Fixture(defaultPanel: true, remembered: new PreviewDockLayoutModel { Side = PreviewDockSide.Left, Size = 280 });

			f.ShowFile();

			Assert.Equal(Constants.Previews.DefaultPanelId, f.Handle.GroupId);
			Assert.Equal(Constants.Previews.DefaultPanelId, f.Handle.Panel.ContentId);
			Assert.Equal("note.md - Preview", f.Handle.Panel.Title);
			Assert.Equal(PreviewDockSide.Left, f.Handle.CurrentLayout!.Side);   // remembered like a group panel
			var content = (PreviewPanelContent)f.Handle.Panel.Content;
			Layout(content);
			Assert.Equal(Visibility.Collapsed, ((FrameworkElement)content.FindName("GroupButtons")!).Visibility);
		});
	}

	[Fact]
	public void AGroupPanels_ButtonsHaveUniqueAutomationNames_PerGroup()
	{
		StaTestRunner.Run(() =>
		{
			using var f = new Fixture();
			f.ShowFile();
			var content = (PreviewPanelContent)f.Handle.Panel.Content;
			Layout(content);

			var names = Descendants<ButtonBase>(content).Select(AutomationProperties.GetName).ToList();
			Assert.Single(names, "Pin panel Text and code");
			Assert.Single(names, "Auto-close panel Text and code");
			Assert.Single(names, "Open in default app from panel Text and code");
			Assert.Single(names, "Show markdown source of panel Text and code");
			Assert.Equal(Visibility.Visible, ((FrameworkElement)content.FindName("GroupButtons")!).Visibility);
			Assert.Equal("PreviewPanel.text-code", AutomationProperties.GetAutomationId(content));
		});
	}

	[Fact]
	public void ThePanelContent_ShowsTheFileRenderer_OrTheBrowser_LikeAWindow()
	{
		StaTestRunner.Run(() =>
		{
			using var f = new Fixture();
			f.ShowFile();
			var content = (PreviewPanelContent)f.Handle.Panel.Content;
			Layout(content);
			var vm = (PreviewWindowViewModel)content.DataContext;
			Assert.Equal(Visibility.Visible, content.Host.FilePane.Visibility);
			Assert.Same(vm.File, content.Host.FilePane.DataContext);
			Assert.Equal(Visibility.Collapsed, content.Host.BrowserHost.Visibility);

			f.Handle.Show(new PreviewItemModel { Kind = PreviewItemKind.Url, Url = "https://example.com/x" }, null, null);
			Layout(content);

			Assert.Equal(Visibility.Collapsed, content.Host.FilePane.Visibility);
			Assert.Equal(Visibility.Visible, content.Host.BrowserHost.Visibility);
			Assert.IsType<BrowserPreviewView>(content.Host.BrowserHost.Child);
			f.Handle.Close();
			Assert.All(f.Content.BrowserSurfaces, s => Assert.True(s.Disposed));
			Assert.Null(content.Host.BrowserHost.Child);
		});
	}

	[Fact]
	public void TheFactory_WithoutADockingArea_SaysSo()
	{
		StaTestRunner.Run(() =>
		{
			var services = new ServiceCollection();
			services.AddSingleton(new DockingSurfaceHost());
			using var provider = services.BuildServiceProvider();

			Assert.Throws<InvalidOperationException>(() => new PreviewDockFactory(provider).Create(Group, false, null));
		});
	}

	private static void Layout(FrameworkElement element, double width = 600, double height = 500)
	{
		element.Measure(new Size(width, height));
		element.Arrange(new Rect(0, 0, width, height));
		element.UpdateLayout();
	}
}
