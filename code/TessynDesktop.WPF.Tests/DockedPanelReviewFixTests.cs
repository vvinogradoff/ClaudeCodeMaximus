using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AvalonDock.Layout;
using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.WPF.Infrastructure;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.State;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.ViewModels;
using TessynDesktop.WPF.Views;

namespace TessynDesktop.WPF.Tests;

/// <summary>The fixes after the review of the docked previews (report 70): the screensaver and floating panels, keyboard focus, a hidden panel's layout, the first-time size.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class DockedPanelReviewFixTests
{
	private static readonly PreviewGroupModel Group = new() { Id = "text-code", Name = "Text and code", Patterns = "md txt" };

	private static (DockedPreviewPanelHandle Handle, PreviewWindowViewModel ViewModel, FakePreviewSettingsService Settings, FakePreviewContentFactory Content) NewHandle(
		IDockingSurface surface, PreviewDockLayoutModel? remembered = null, ManualTimeProvider? time = null)
	{
		var settings = new FakePreviewSettingsService(Group);
		var content = new FakePreviewContentFactory();
		var vm = new PreviewWindowViewModel(Group.Id, Group.Name, false, settings, content, new ImmediateUiDispatcher());
		var panelContent = new PreviewPanelContent();
		panelContent.Attach(vm, NullLogger.Instance, true);
		var panel = new LayoutAnchorable { ContentId = Group.Id, Content = panelContent };
		return (new DockedPreviewPanelHandle(vm, panelContent, panel, surface, settings, remembered, new ImmediateUiDispatcher(), NullLogger.Instance, time), vm, settings, content);
	}

	private static void Settle(Window window)
	{
		window.UpdateLayout();
		window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
		window.UpdateLayout();
	}

	private static PreviewItemModel Md(string name = "note.md") => new() { Kind = PreviewItemKind.File, FileName = name };

	// ---- 2: the screensaver and floating panels ----

	[Fact]
	public void FloatingPanels_AreHiddenWhileTheCoverIsUp_AndComeBack_AndANewOneStartsHidden()
	{
		StaTestRunner.Run(() =>
		{
			var area = new DockingArea(new Border { MinWidth = 100 });
			var window = new Window { Content = area, Width = 1200, Height = 700, Left = -3000, Top = -3000, ShowActivated = false, ShowInTaskbar = false };
			window.Show();
			Settle(window);
			var applier = new PreviewDockLayoutApplier();
			var first = new LayoutAnchorable { ContentId = "a", Title = "a", Content = new TextBlock { Text = "a" } };
			applier.Apply(area.Root, area.DocumentPane, first, new PreviewDockLayoutModel
			{
				Floating = true, FloatingPlacement = new PreviewPlacementModel { Left = -3000, Top = -3000, Width = 300, Height = 300 },
			}, 1200, 700);
			Settle(window);
			Assert.True(first.IsFloating);
			Assert.Single(area.Manager.FloatingWindows);

			area.SetFloatingVisible(false);   // the cover goes up
			Assert.All(area.Manager.FloatingWindows, w => Assert.Equal(Visibility.Hidden, w.Visibility));

			var second = new LayoutAnchorable { ContentId = "b", Title = "b", Content = new TextBlock { Text = "b" } };
			applier.Apply(area.Root, area.DocumentPane, second, new PreviewDockLayoutModel
			{
				Floating = true, FloatingPlacement = new PreviewPlacementModel { Left = -3000, Top = -2900, Width = 300, Height = 300 },
			}, 1200, 700);
			Settle(window);
			Assert.Equal(2, area.Manager.FloatingWindows.Count());
			Assert.All(area.Manager.FloatingWindows, w => Assert.Equal(Visibility.Hidden, w.Visibility));   // created while covered

			area.SetFloatingVisible(true);   // the cover goes down
			Assert.All(area.Manager.FloatingWindows, w => Assert.Equal(Visibility.Visible, w.Visibility));
			window.Close();
		});
	}

	[Fact]
	public void TheScreensaverCover_TellsTheDockingSurface_WhenItGoesUpAndDown()
	{
		StaTestRunner.Run(() =>
		{
			var store = new ClientSettingsStore(new MemoryClientState(), NullLogger<ClientSettingsStore>.Instance);
			store.Update<ScreensaverSettings>(s =>
			{
				s.Directory = @"D:\pics";
				s.TimeoutSeconds = 5;
			});
			var t0 = new DateTime(2026, 11, 16, 9, 0, 0, DateTimeKind.Utc);
			var controller = new ScreensaverController(store, new OneImage(), t0);
			var surface = new TestDockingSurface();
			var cover = new ScreensaverOverlay { Covered = new Border() };
			cover.CoverChanged += (_, covered) => surface.SetFloatingVisible(!covered);
			cover.Attach(controller, clock: () => t0);

			controller.Tick(t0.AddSeconds(5));
			cover.Refresh();
			Assert.False(surface.FloatingVisible);

			cover.NoteMouseMove(new Point(0, 0));
			cover.NoteMouseMove(new Point(90, 80));
			cover.Refresh();
			Assert.True(surface.FloatingVisible);
			cover.Detach();
		});
	}

	private sealed class OneImage : IScreensaverImages
	{
		public IReadOnlyList<string> List(string directory) => ["a.png"];
	}

	// ---- 3: no keyboard focus ----

	[Fact]
	public void OpeningAndReplacing_APanel_LeavesTheComposersFocusAlone()
	{
		StaTestRunner.Run(() =>
		{
			var composer = new TextBox { Name = "Composer" };
			var area = new DockingArea(new Grid { Children = { composer } });
			var window = new Window { Content = area, Width = 1200, Height = 700, Left = -3000, Top = -3000, ShowActivated = false, ShowInTaskbar = false };
			window.Show();
			Settle(window);
			FocusManager.SetFocusedElement(window, composer);
			var (handle, _, _, content) = NewHandle(area);
			using var _ = content;

			handle.Show(Md(), new PreviewTarget("h", "note.md"), new FakeTessynClient());
			Settle(window);
			Assert.Same(composer, FocusManager.GetFocusedElement(window));

			handle.Show(Md("other.md"), new PreviewTarget("h", "other.md"), new FakeTessynClient());
			Settle(window);
			Assert.Same(composer, FocusManager.GetFocusedElement(window));
			Assert.False(handle.Panel.IsActive);
			Assert.True(handle.Panel.IsSelected);
			handle.Close();
			Settle(window);
			Assert.Same(composer, FocusManager.GetFocusedElement(window));
			window.Close();
		});
	}

	[Fact]
	public void TheDockCodePath_NeverFocusesOrActivates()
	{
		foreach (var file in new[] { "DockedPreviewPanelHandle.cs", "PreviewDockFactory.cs", "PreviewDockLayoutApplier.cs", "PreviewDockLayoutReader.cs", "DockingArea.xaml.cs" })
		{
			var path = Path.Combine(ViewsFolder(), file);
			var code = File.ReadAllText(path);
			Assert.DoesNotContain(".Focus(", code);
			Assert.DoesNotContain("IsActive = true", code);
			Assert.DoesNotContain("Activate(", code);
		}
	}

	private static string ViewsFolder([CallerFilePath] string here = "") =>
		Path.Combine(Path.GetDirectoryName(here)!, "..", "TessynDesktop.WPF", "Views");

	// ---- 4: a hidden panel keeps the last good layout ----

	[Fact]
	public void TheReader_OnAHiddenPanel_SaysItIsNotShown_AndHasNoPlace()
	{
		var surface = new TestDockingSurface();
		var panel = new LayoutAnchorable { ContentId = "a", Content = "a" };
		new PreviewDockLayoutApplier().Apply(surface.Root, surface.DocumentPane, panel, new PreviewDockLayoutModel { Side = PreviewDockSide.Left, Size = 260 }, 1000, 600);

		panel.Hide();

		var read = new PreviewDockLayoutReader().Read(panel, surface.DocumentPane);
		Assert.False(read.Shown);
		Assert.Equal(0, read.Size);   // why the handle must not trust it
	}

	[Fact]
	public void HidingAPanel_KeepsItsLastGoodLayout_ForTheGroup()
	{
		StaTestRunner.Run(() =>
		{
			var surface = new TestDockingSurface();
			var (handle, _, settings, content) = NewHandle(surface, new PreviewDockLayoutModel { Side = PreviewDockSide.Left, Size = 260 });
			using var _ = content;
			handle.Show(Md(), new PreviewTarget("h", "note.md"), new FakeTessynClient());

			handle.Panel.Hide();   // the layout events of the hide reach the handle before it finishes

			var saved = settings.DockLayouts[Group.Id];
			Assert.False(saved.Shown);
			Assert.Equal(PreviewDockSide.Left, saved.Side);
			Assert.Equal(260, saved.Size);
		});
	}

	// ---- 5: the first-time size waits for a measured area ----

	[Fact]
	public void FirstTime_WithAnUnmeasuredArea_TakesTheFortyPercentOnceItHasAWidth_AndSavesNothing()
	{
		StaTestRunner.Run(() =>
		{
			var time = new ManualTimeProvider();
			var surface = new TestDockingSurface(width: 0, height: 0);
			var (handle, _, settings, content) = NewHandle(surface, time: time);
			using var _ = content;
			handle.Show(Md(), new PreviewTarget("h", "note.md"), new FakeTessynClient());
			var pane = (LayoutAnchorablePane)handle.Panel.Parent!;
			Assert.Equal(Constants.PreviewDock.FallbackPaneSize, pane.DockWidth.Value);

			surface.RaiseLayoutChanged();   // still unmeasured
			Assert.Equal(Constants.PreviewDock.FallbackPaneSize, pane.DockWidth.Value);

			surface.AreaWidth = 1000;
			surface.AreaHeight = 600;
			surface.RaiseLayoutChanged();

			Assert.Equal(400, pane.DockWidth.Value);
			time.Advance(TimeSpan.FromSeconds(1));
			Assert.Empty(settings.DockLayouts);   // opening at the first-time size is not a change of the user's
			handle.Close();
		});
	}

	[Fact]
	public void ARememberedSize_IsNeverReplacedByTheFirstTimeSize()
	{
		StaTestRunner.Run(() =>
		{
			var surface = new TestDockingSurface(width: 0, height: 0);
			var (handle, _, _, content) = NewHandle(surface, new PreviewDockLayoutModel { Side = PreviewDockSide.Right, Size = 333 });
			using var _ = content;
			handle.Show(Md(), new PreviewTarget("h", "note.md"), new FakeTessynClient());

			surface.AreaWidth = 1000;
			surface.RaiseLayoutChanged();

			Assert.Equal(333, ((LayoutAnchorablePane)handle.Panel.Parent!).DockWidth.Value);
			handle.Close();
		});
	}
}
