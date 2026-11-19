using System.Windows;
using System.Windows.Controls;
using AvalonDock.Layout;
using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.State;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.Views;

namespace TessynDesktop.WPF.Tests;

/// <summary>
/// FR.6.11, FR.6.12, FR.6.14, FR.6.18, FR.6.19, FR.6.22 (stage 1, the layout core): the docking area as the whole shell layout on an STA thread: the default layout, the
/// <see cref="IShellLayout"/> calls, the saved layout and its migration, Reset layout and the previews' first place next to Focus.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class DockedShellLayoutTests
{
	private const string DefaultState = "sessions=left,tab0,open,selected recent=left,tab1,open tags=left,tab2,open session=center focus=right,tab0,open,selected";

	private static readonly MonitorBounds Screen = new(0, 0, 1920, 1080);

	private sealed class Rig
	{
		public Rig(bool persistent = true, MemoryClientState? state = null, double width = 1400)
		{
			Width = width;
			State = state ?? new MemoryClientState();
			Settings = new ClientSettingsStore(State, NullLogger<ClientSettingsStore>.Instance);
			Persistent = persistent;
		}

		public MemoryClientState State { get; }

		public double Width { get; }

		public ClientSettingsStore Settings { get; }

		public bool Persistent { get; }

		public CapturingLogger<DockingArea> Log { get; } = new();

		public DockingArea Area { get; private set; } = null!;

		public Window Window { get; private set; } = null!;

		public Rig Start() => Build().Show();

		/// <summary>The docking area with its shell layout built, not shown yet.</summary>
		public Rig Build()
		{
			Area = new DockingArea(new Border { Name = "Stub", MinWidth = 100 });
			Window = new Window { Content = Area, Width = Width, Height = 800, Left = -3000, Top = -3000, ShowActivated = false, ShowInTaskbar = false };
			Area.BuildShell(Panels(), new ShellLayoutMemory(Settings, State, Persistent), Log, () => ([Screen], Screen));
			return this;
		}

		public Rig Show()
		{
			Window.Show();
			Settle();
			return this;
		}

		public void Settle()
		{
			Window.UpdateLayout();
			Window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
			Window.UpdateLayout();
		}

		/// <summary>Lets the dispatcher run for a while (the layout's 500 ms timers).</summary>
		public void Wait(int milliseconds)
		{
			var frame = new System.Windows.Threading.DispatcherFrame();
			var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
			timer.Tick += (_, _) =>
			{
				timer.Stop();
				frame.Continue = false;
			};
			timer.Start();
			System.Windows.Threading.Dispatcher.PushFrame(frame);
		}

		public void Stop()
		{
			Area.FlushLayout();
			Window.Close();
		}

		public LayoutAnchorable Panel(string id) => Area.FindPanel(id)!;

		public LayoutAnchorablePane PaneOf(string id) => (LayoutAnchorablePane)Panel(id).Parent;

		private static List<ShellPanelDefinition> Panels() =>
		[
			Panel(Constants.DockedShell.SessionsPanelId, Constants.ShellLayout.SessionsTitle, true),
			Panel(Constants.DockedShell.RecentPanelId, "Recent", true),
			Panel(Constants.DockedShell.TagsPanelId, "Tags", true),
			Panel(Constants.DockedShell.FocusPanelId, "Focus", false),
		];

		private static ShellPanelDefinition Panel(string id, string title, bool left) => new() { Id = id, Title = title, Content = new TextBlock { Text = id }, IsLeft = left };
	}

	private static string WithoutSelection(string state) => state.Replace(",selected", string.Empty);

	private static LayoutAnchorable Preview(string id) => new() { ContentId = id, Title = id, Content = new TextBlock { Text = id } };

	[Fact]
	public void TheDefaultLayout_HasTheLeftPaneTheSessionViewAndFocus_WithTheDocumentedSizes()
	{
		StaTestRunner.Run(() =>
		{
			var rig = new Rig().Start();
			var area = rig.Area;

			Assert.Equal(DefaultState, area.StateText());
			Assert.Same(rig.PaneOf(Constants.DockedShell.SessionsPanelId), rig.PaneOf(Constants.DockedShell.TagsPanelId));
			Assert.Equal(320, rig.PaneOf(Constants.DockedShell.SessionsPanelId).DockWidth.Value);
			Assert.True(rig.Panel(Constants.DockedShell.SessionsPanelId).IsSelected);
			var document = Assert.Single(area.Root.Descendents().OfType<LayoutDocument>());
			Assert.False(document.CanClose);
			Assert.False(document.CanFloat);
			Assert.Equal(Constants.DockedShell.SessionPanelId, document.ContentId);
			Assert.Equal(Constants.DockedShell.SessionPanelId, Constants.PreviewDock.SessionDocumentId);

			var focus = rig.PaneOf(Constants.DockedShell.FocusPanelId);
			Assert.InRange(focus.DockWidth.Value, area.ActualWidth * 0.35 - 2, area.ActualWidth * 0.35 + 2);   // Focus at 35 % of the docking area's width
			Assert.True(area.SessionView.ActualWidth > 300);   // the session view keeps the rest
			Assert.Contains(Constants.ShellLayout.LayoutLogFormat.Replace("{State}", DefaultState), rig.Log.Messages);
			rig.Stop();
		});
	}

	[Fact]
	public void TheLayoutCalls_CloseAndBringAPanelForward_AtItsLastPlace_AndNeverCloseTheSessionView()
	{
		StaTestRunner.Run(() =>
		{
			var rig = new Rig().Start();
			var area = rig.Area;
			var changes = 0;
			area.Changed += (_, _) => changes++;

			area.Close(Constants.DockedShell.RecentPanelId);
			rig.Settle();
			Assert.False(area.IsOpen(Constants.DockedShell.RecentPanelId));
			Assert.Contains("recent=closed", area.StateText());
			Assert.True(changes > 0);

			area.Close(Constants.DockedShell.SessionPanelId);   // not closable
			Assert.True(area.IsOpen(Constants.DockedShell.SessionPanelId));

			area.BringForward(Constants.DockedShell.RecentPanelId);
			rig.Settle();
			Assert.True(area.IsOpen(Constants.DockedShell.RecentPanelId));
			Assert.StartsWith("sessions=left,tab0,open", area.StateText());
			Assert.Contains("recent=left,tab1,open,selected", area.StateText());   // its last place: the left pane, its tab, brought to the front
			Assert.Same(rig.PaneOf(Constants.DockedShell.SessionsPanelId), rig.PaneOf(Constants.DockedShell.RecentPanelId));

			area.BringForward(Constants.DockedShell.TagsPanelId);   // behind another tab: selected
			Assert.True(rig.Panel(Constants.DockedShell.TagsPanelId).IsSelected);
			rig.Stop();
		});
	}

	[Fact]
	public void APanelHiddenByTheLibrary_IsShownAgainAtItsLastPlace()
	{
		StaTestRunner.Run(() =>
		{
			var rig = new Rig().Start();
			var tags = rig.Panel(Constants.DockedShell.TagsPanelId);
			tags.Hide();   // the library's own Hide (the tool window's X) is a close too
			rig.Settle();
			Assert.False(rig.Area.IsOpen(Constants.DockedShell.TagsPanelId));
			Assert.Contains("tags=closed", rig.Area.StateText());

			rig.Area.BringForward(Constants.DockedShell.TagsPanelId);
			rig.Settle();

			Assert.True(rig.Area.IsOpen(Constants.DockedShell.TagsPanelId));
			Assert.Contains("tags=left,tab2,open", rig.Area.StateText());
			rig.Stop();
		});
	}

	[Fact]
	public void AutoHide_TogglesThePaneThatHoldsThePanel_AndABroughtForwardPanelStaysAutoHidden()
	{
		StaTestRunner.Run(() =>
		{
			var rig = new Rig().Start();
			var area = rig.Area;
			Assert.True(area.CanToggleAutoHide(Constants.DockedShell.SessionsPanelId));
			Assert.False(area.IsAutoHidden(Constants.DockedShell.SessionsPanelId));

			area.ToggleAutoHide(Constants.DockedShell.SessionsPanelId);
			rig.Settle();
			Assert.True(area.IsAutoHidden(Constants.DockedShell.SessionsPanelId));
			Assert.True(area.IsAutoHidden(Constants.DockedShell.RecentPanelId));   // the whole pane
			Assert.Contains("sessions=left,tab0,autohide", area.StateText());

			area.BringForward(Constants.DockedShell.RecentPanelId);   // selected, slid out; stays auto-hidden
			rig.Settle();
			Assert.True(area.IsAutoHidden(Constants.DockedShell.RecentPanelId));

			area.ToggleAutoHide(Constants.DockedShell.SessionsPanelId);
			rig.Settle();
			Assert.False(area.IsAutoHidden(Constants.DockedShell.SessionsPanelId));

			area.Close(Constants.DockedShell.SessionsPanelId);
			Assert.False(area.CanToggleAutoHide(Constants.DockedShell.SessionsPanelId));   // closed: nothing to toggle
			rig.Stop();
		});
	}

	[Fact]
	public void TheLayout_IsSavedAndRestored_WithPositionsAndClosedPanels_AndPreviewPanelsAreLeftOut()
	{
		StaTestRunner.Run(() =>
		{
			var first = new Rig().Start();
			var preview = Preview("images");
			new PreviewDockLayoutApplier().Apply(first.Area.Root, first.Area.DocumentPane, preview, null, first.Area.AreaWidth, first.Area.AreaHeight);
			first.Settle();
			Assert.Same(first.PaneOf(Constants.DockedShell.FocusPanelId), preview.Parent);   // a tab next to Focus
			first.Area.Close(Constants.DockedShell.TagsPanelId);
			first.Area.ToggleAutoHide(Constants.DockedShell.SessionsPanelId);
			first.Settle();
			Assert.True(first.Area.IsAutoHidden(Constants.DockedShell.SessionsPanelId));
			var expected = first.Area.StateText();
			first.Stop();

			var saved = first.Settings.Get<DockedShellLayoutSettings>();
			Assert.Equal(Constants.DockedShell.LayoutVersion, saved.Version);
			Assert.NotNull(saved.Layout);
			Assert.DoesNotContain("images", saved.Layout);   // preview panels are never in shellLayout (FR.6.18)

			var second = new Rig(state: first.State).Start();
			Assert.Equal(WithoutSelection(expected), WithoutSelection(second.Area.StateText()));
			Assert.Contains("tags=closed", second.Area.StateText());
			Assert.DoesNotContain(second.Area.Root.Descendents().OfType<LayoutAnchorable>(), a => a.ContentId == "images");
			Assert.Contains(Constants.ShellLayout.LayoutRestoredLog, second.Log.Messages);
			second.Area.BringForward(Constants.DockedShell.TagsPanelId);   // a panel closed in the saved layout comes back at its place
			second.Settle();
			Assert.True(second.Area.IsOpen(Constants.DockedShell.TagsPanelId), second.Area.StateText() + " | " + saved.Layout);
			Assert.Contains("tags=left", second.Area.StateText());
			second.Stop();
		});
	}

	[Fact]
	public void ASavedPaneSize_IsRestored()
	{
		StaTestRunner.Run(() =>
		{
			var first = new Rig().Start();
			first.PaneOf(Constants.DockedShell.SessionsPanelId).DockWidth = new GridLength(410);
			first.Settle();
			first.Stop();

			var second = new Rig(state: first.State).Start();
			Assert.Equal(410, second.PaneOf(Constants.DockedShell.SessionsPanelId).DockWidth.Value);
			second.Stop();
		});
	}

	[Fact]
	public void ALayoutWithAnotherVersion_OrOneThatCannotBeRead_GivesTheDefaultLayout_AndTheLogSaysWhy()
	{
		StaTestRunner.Run(() =>
		{
			var state = new MemoryClientState();
			var settings = new ClientSettingsStore(state, NullLogger<ClientSettingsStore>.Instance);
			settings.Update<DockedShellLayoutSettings>(s =>
			{
				s.Version = Constants.DockedShell.LayoutVersion + 1;
				s.Layout = "<LayoutRoot />";
			});
			var newer = new Rig(state: state).Start();
			Assert.Equal(DefaultState, newer.Area.StateText());
			Assert.Contains(newer.Log.Messages, m => m.Contains(string.Format(Constants.ShellLayout.ReasonVersionFormat, Constants.DockedShell.LayoutVersion + 1, Constants.DockedShell.LayoutVersion)));
			newer.Window.Close();

			settings.Update<DockedShellLayoutSettings>(s =>
			{
				s.Version = Constants.DockedShell.LayoutVersion;
				s.Layout = "this is not a layout";
			});
			var damaged = new Rig(state: state).Start();
			Assert.Equal(DefaultState, damaged.Area.StateText());
			Assert.Contains(damaged.Log.Messages, m => m.Contains("cannot be read"));
			damaged.Window.Close();
		});
	}

	[Fact]
	public void WithoutASavedLayout_TheOldTreeWidthAndCollapseAreTakenOverOnce_AndTheOldValuesStay()
	{
		StaTestRunner.Run(() =>
		{
			var state = new MemoryClientState();
			state.State.TreeWidth = 410;
			var settings = new ClientSettingsStore(state, NullLogger<ClientSettingsStore>.Instance);
			settings.Update<ShellLayoutSettings>(s => s.TreeCollapsed = true);

			var first = new Rig(state: state).Start();
			Assert.Contains("sessions=left,tab0,autohide", first.Area.StateText());   // the collapsed tree is an auto-hidden left pane
			Assert.Equal(410, first.Panel(Constants.DockedShell.SessionsPanelId).AutoHideWidth);   // the slide-out has the tree width
			first.Area.ToggleAutoHide(Constants.DockedShell.SessionsPanelId);   // the user expands it, and the layout is saved
			first.Settle();
			Assert.Equal(410, first.PaneOf(Constants.DockedShell.SessionsPanelId).DockWidth.Value);
			first.Stop();
			Assert.Equal(410, state.State.TreeWidth);
			Assert.True(settings.Get<ShellLayoutSettings>().TreeCollapsed);   // not deleted

			var second = new Rig(state: state).Start();   // now the saved layout rules, not the old values
			Assert.Contains("sessions=left,tab0,open", second.Area.StateText());
			second.Stop();
		});
	}

	[Fact]
	public void ATestWindowWithoutATestDataRoot_UsesTheDefaultLayout_AndNeverWritesOne()
	{
		StaTestRunner.Run(() =>
		{
			var state = new MemoryClientState();
			var settings = new ClientSettingsStore(state, NullLogger<ClientSettingsStore>.Instance);
			settings.Update<DockedShellLayoutSettings>(s =>
			{
				s.Version = Constants.DockedShell.LayoutVersion;
				s.Layout = "<ignored/>";
			});
			var rig = new Rig(persistent: false, state: state).Start();
			Assert.Equal(DefaultState, rig.Area.StateText());
			rig.Area.Close(Constants.DockedShell.FocusPanelId);
			rig.Settle();
			rig.Stop();

			Assert.Equal("<ignored/>", settings.Get<DockedShellLayoutSettings>().Layout);
		});
	}

	[Fact]
	public void AFloatingPanelOffEveryMonitor_IsMovedOntoOneWhenTheLayoutIsRestored()
	{
		StaTestRunner.Run(() =>
		{
			var first = new Rig().Start();
			var focus = first.Panel(Constants.DockedShell.FocusPanelId);
			focus.FloatingLeft = -40000;
			focus.FloatingTop = -40000;
			focus.FloatingWidth = 500;
			focus.FloatingHeight = 400;
			focus.Float();
			first.Settle();
			Assert.Contains("focus=floating", first.Area.StateText());
			first.Stop();
			first.Settings.Update<DockedShellLayoutSettings>(s => s.Layout = System.Text.RegularExpressions.Regex.Replace(s.Layout!, "Floating(Left|Top)=\"[-0-9.]+\"", "Floating$1=\"-40000\""));   // the monitor it was on is gone

			var second = new Rig(state: first.State).Build();
			var restored = second.Panel(Constants.DockedShell.FocusPanelId);
			Assert.True(restored.IsFloating);
			Assert.InRange(restored.FloatingLeft, 0, 1920);
			Assert.InRange(restored.FloatingTop, 0, 1080);
			Assert.Contains(second.Log.Messages, m => m.Contains("focus") && m.Contains("off every monitor"));
			second.Show();   // the floating window opens (its position is the library's from here on)
			Assert.True(restored.IsFloating);
			second.Window.Close();
		});
	}

	[Fact]
	public void ResetLayout_RestoresTheDefaultLayout_KeepsOpenPreviewsAtTheirDefaultPlace_AndSavesAtOnce()
	{
		StaTestRunner.Run(() =>
		{
			var rig = new Rig().Start();
			var area = rig.Area;
			var preview = Preview("images");
			new PreviewDockLayoutApplier().Apply(area.Root, area.DocumentPane, preview, new PreviewDockLayoutModel { Side = PreviewDockSide.Bottom, Size = 200 }, area.AreaWidth, area.AreaHeight);
			area.Close(Constants.DockedShell.FocusPanelId);
			area.Close(Constants.DockedShell.RecentPanelId);
			rig.PaneOf(Constants.DockedShell.TagsPanelId).DockWidth = new GridLength(500);
			area.ToggleAutoHide(Constants.DockedShell.SessionsPanelId);
			rig.Settle();
			Assert.NotEqual(DefaultState, area.StateText());

			area.ResetLayout();
			rig.Settle();

			Assert.Equal(WithoutSelection(DefaultState), WithoutSelection(area.StateText()));
			Assert.True(preview.IsSelected);   // the preview came in as the selected tab of Focus's pane
			Assert.Equal(320, rig.PaneOf(Constants.DockedShell.SessionsPanelId).DockWidth.Value);
			Assert.Same(rig.PaneOf(Constants.DockedShell.FocusPanelId), preview.Parent);   // the open preview goes to the default place for a preview: next to Focus
			Assert.True(area.IsOpen(Constants.DockedShell.FocusPanelId));
			Assert.Contains(Constants.ShellLayout.LayoutResetLog, rig.Log.Messages);
			var saved = rig.Settings.Get<DockedShellLayoutSettings>();   // saved at once, no wait for the 500 ms
			Assert.NotNull(saved.Layout);
			Assert.DoesNotContain("autohide", area.StateText());
			rig.Window.Close();
		});
	}

	[Fact]
	public void APreviewWithoutARememberedLayout_OpensAsATabInTheFocusPane_ElseRightAt40Percent()
	{
		StaTestRunner.Run(() =>
		{
			var rig = new Rig().Start();
			var area = rig.Area;
			var first = Preview("a");
			new PreviewDockLayoutApplier().Apply(area.Root, area.DocumentPane, first, null, area.AreaWidth, area.AreaHeight);
			rig.Settle();
			Assert.Same(rig.PaneOf(Constants.DockedShell.FocusPanelId), first.Parent);
			Assert.True(first.IsSelected);
			Assert.Equal(1, ((LayoutAnchorablePane)first.Parent).Children.ToList().IndexOf(first));

			area.Close(Constants.DockedShell.FocusPanelId);   // Focus closed: the old rule
			var second = Preview("b");
			new PreviewDockLayoutApplier().Apply(area.Root, area.DocumentPane, second, null, area.AreaWidth, area.AreaHeight);
			rig.Settle();
			Assert.NotSame(first.Parent, second.Parent);
			Assert.Equal(PreviewDockSide.Right, new PreviewDockLayoutReader().Read(second, area.DocumentPane).Side);
			Assert.InRange(((LayoutAnchorablePane)second.Parent).DockWidth.Value, area.ActualWidth * 0.4 - 2, area.ActualWidth * 0.4 + 2);
			rig.Window.Close();
		});
	}

	[Fact]
	public void APreviewWithoutARememberedLayout_WhileFocusFloats_DocksRightAt40Percent()
	{
		StaTestRunner.Run(() =>
		{
			var rig = new Rig().Start();
			var focus = rig.Panel(Constants.DockedShell.FocusPanelId);
			focus.FloatingLeft = 100;
			focus.FloatingTop = 100;
			focus.FloatingWidth = 400;
			focus.FloatingHeight = 400;
			focus.Float();
			rig.Settle();

			var preview = Preview("a");
			new PreviewDockLayoutApplier().Apply(rig.Area.Root, rig.Area.DocumentPane, preview, null, rig.Area.AreaWidth, rig.Area.AreaHeight);
			rig.Settle();

			Assert.False(preview.IsFloating);
			Assert.Equal(PreviewDockSide.Right, new PreviewDockLayoutReader().Read(preview, rig.Area.DocumentPane).Side);
			rig.Window.Close();
		});
	}

	[Fact]
	public void AResizeIsSavedButNotLogged_AndAMoveIsLoggedOnce()
	{
		StaTestRunner.Run(() =>
		{
			var rig = new Rig().Start();
			var lines = rig.Log.Messages.Count(m => m.StartsWith("Shell layout:"));
			rig.PaneOf(Constants.DockedShell.SessionsPanelId).DockWidth = new GridLength(380);
			rig.Settle();
			Assert.Equal(lines, rig.Log.Messages.Count(m => m.StartsWith("Shell layout:")));

			rig.Area.Close(Constants.DockedShell.TagsPanelId);
			rig.Settle();
			Assert.Equal(lines + 1, rig.Log.Messages.Count(m => m.StartsWith("Shell layout:")));
			Assert.Contains("Shell layout: sessions=left,tab0,open,selected recent=left,tab1,open tags=closed session=center focus=right,tab0,open,selected", rig.Log.Messages);
			rig.Stop();
			Assert.NotNull(rig.Settings.Get<DockedShellLayoutSettings>().Layout);
		});
	}

	[Fact]
	public void ALayoutPassWithoutADockingChange_ReadsNothing_AndSavesNothing()
	{
		StaTestRunner.Run(() =>
		{
			var rig = new Rig().Start();
			rig.Wait(700);   // the first probe runs and finds nothing to save
			var captures = rig.Area.CaptureCount;

			rig.Area.InvalidateMeasure();
			rig.Settle();
			rig.Area.SessionView.InvalidateMeasure();
			rig.Settle();
			Assert.Equal(captures, rig.Area.CaptureCount);   // the pass itself reads nothing
			rig.Wait(700);

			Assert.Equal(captures, rig.Area.CaptureCount);   // and the probe finds no change
			Assert.Null(rig.Settings.Get<DockedShellLayoutSettings>().Layout);   // so nothing was saved
			rig.Window.Close();
		});
	}

	[Fact]
	public void ASplitterMove_IsFoundByTheProbe_AndSaved()
	{
		StaTestRunner.Run(() =>
		{
			var rig = new Rig().Start();
			rig.Wait(700);
			rig.PaneOf(Constants.DockedShell.SessionsPanelId).DockWidth = new GridLength(390);
			rig.Settle();
			rig.Wait(1300);   // the probe, then the save delay

			Assert.NotNull(rig.Settings.Get<DockedShellLayoutSettings>().Layout);
			rig.Window.Close();
		});
	}

	[Fact]
	public void FixedPaneWidthsFromABiggerScreen_AreShrunkProportionally_SoTheSessionViewKeepsItsMinimum()
	{
		StaTestRunner.Run(() =>
		{
			var first = new Rig().Start();
			first.PaneOf(Constants.DockedShell.SessionsPanelId).DockWidth = new GridLength(700);
			first.PaneOf(Constants.DockedShell.FocusPanelId).DockWidth = new GridLength(500);
			first.Settle();
			first.Stop();

			var second = new Rig(state: first.State, width: 1000).Start();
			var left = second.PaneOf(Constants.DockedShell.SessionsPanelId).DockWidth.Value;
			var focus = second.PaneOf(Constants.DockedShell.FocusPanelId).DockWidth.Value;
			Assert.True(left + focus <= 1000 - Constants.ShellLayout.MinSessionWidth + 2, $"{left} + {focus}");
			Assert.InRange(left / focus, 1.3, 1.5);   // the proportion stays (700 : 500)
			Assert.True(second.Area.SessionView.ActualWidth >= Constants.ShellLayout.MinSessionWidth - 20, second.Area.SessionView.ActualWidth.ToString());
			second.Window.Close();
		});
	}

	[Fact]
	public void AClosedPanel_SurvivesTwoRestartsWithoutADuplicateInTheSavedText()
	{
		StaTestRunner.Run(() =>
		{
			var first = new Rig().Start();
			first.Area.Close(Constants.DockedShell.TagsPanelId);
			first.Settle();
			first.Stop();
			Assert.Equal(1, Count(first.Settings.Get<DockedShellLayoutSettings>().Layout!, "ContentId=\"tags\""));

			var second = new Rig(state: first.State).Start();
			Assert.Equal(1, second.Area.Root.Hidden.Count(h => h.ContentId == Constants.DockedShell.TagsPanelId));
			second.Area.BringForward(Constants.DockedShell.TagsPanelId);
			second.Settle();
			Assert.Empty(second.Area.Root.Hidden.Where(h => h.ContentId == Constants.DockedShell.TagsPanelId));
			second.Area.Close(Constants.DockedShell.TagsPanelId);
			second.Settle();
			second.Stop();
			Assert.Equal(1, Count(second.Settings.Get<DockedShellLayoutSettings>().Layout!, "ContentId=\"tags\""));

			var third = new Rig(state: first.State).Start();
			Assert.Contains("tags=closed", third.Area.StateText());
			Assert.Equal(1, third.Area.Root.Hidden.Count(h => h.ContentId == Constants.DockedShell.TagsPanelId));
			third.Window.Close();
		});
	}

	private static int Count(string text, string part) => text.Split(part).Length - 1;

	[Fact]
	public void ALayoutOfANewerVersion_IsKeptUntilTheFirstRealChange()
	{
		StaTestRunner.Run(() =>
		{
			var state = new MemoryClientState();
			var settings = new ClientSettingsStore(state, NullLogger<ClientSettingsStore>.Instance);
			settings.Update<DockedShellLayoutSettings>(s =>
			{
				s.Version = Constants.DockedShell.LayoutVersion + 1;
				s.Layout = "<newer layout/>";
			});
			var rig = new Rig(state: state).Start();
			rig.Wait(700);
			rig.Area.FlushLayout();
			var kept = settings.Get<DockedShellLayoutSettings>();
			Assert.Equal("<newer layout/>", kept.Layout);   // the default is used, the stored text is left alone
			Assert.Equal(Constants.DockedShell.LayoutVersion + 1, kept.Version);

			rig.Area.Close(Constants.DockedShell.FocusPanelId);   // the user changes the layout
			rig.Settle();
			rig.Area.FlushLayout();
			var replaced = settings.Get<DockedShellLayoutSettings>();
			Assert.Equal(Constants.DockedShell.LayoutVersion, replaced.Version);
			Assert.NotEqual("<newer layout/>", replaced.Layout);
			rig.Window.Close();
		});
	}

	[Fact]
	public void BringingAnAutoHiddenPanelForward_OpensTheSlideOut()
	{
		StaTestRunner.Run(() =>
		{
			var rig = new Rig().Start();
			rig.Area.ToggleAutoHide(Constants.DockedShell.SessionsPanelId);
			rig.Settle();
			Assert.False(rig.Area.Chrome.SlideOut.IsOpen);

			rig.Area.BringForward(Constants.DockedShell.RecentPanelId);
			rig.Settle();

			Assert.True(rig.Area.Chrome.SlideOut.IsOpen);   // the synthetic click on the edge tab is what the library reacts to
			Assert.DoesNotContain(rig.Log.Messages, m => m.Contains("did not open"));
			rig.Window.Close();
		});
	}
}
