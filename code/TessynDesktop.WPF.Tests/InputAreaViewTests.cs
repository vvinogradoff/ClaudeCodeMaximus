using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.WPF.Infrastructure;
using TessynDesktop.WPF.Rendering;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.ViewModels;
using TessynDesktop.WPF.Views;

namespace TessynDesktop.WPF.Tests;

/// <summary>
/// FR.12.29 and FR.23.12 on the real session pane (an STA thread, TC.IC.11 to 20): the splitter above the input area, the collapsed and expanded layouts, the single side button,
/// the Session/Focus switch that swaps the transcript for the Focus view, and the settings toggle.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class InputAreaViewTests
{
	private sealed record Fixture(SessionPane Pane, SessionPaneViewModel Model)
	{
		public T Find<T>(string name) where T : class => (T)(Pane.FindName(name) ?? throw new InvalidOperationException(name + " not found"));

		public void Settle()
		{
			for (var i = 0; i < 3; i++)
				Pane.UpdateLayout();
		}

		public GridSplitter Splitter => Find<GridSplitter>("InputSplitter");

		public RowDefinition InputRow => Find<RowDefinition>("InputRow");

		public TextBox Input => Find<TextBox>("InputBox");

		public SlotHost CommandBar => Slots(Pane).Single(s => s.SlotId == Constants.Slots.CommandBar);
	}

	private static IEnumerable<SlotHost> Slots(DependencyObject root)
	{
		var count = VisualTreeHelper.GetChildrenCount(root);
		for (var i = 0; i < count; i++)
		{
			var child = VisualTreeHelper.GetChild(root, i);
			if (child is SlotHost host)
				yield return host;

			foreach (var nested in Slots(child))
				yield return nested;
		}
	}

	private static Fixture Create(double? storedHeight = null)
	{
		var client = new FakeTessynClient();
		client.Sessions["a"] = FakeSession.WithEntries(3);
		client.Sessions["b"] = FakeSession.WithEntries(3);
		var model = new SessionPaneViewModel(client, new FakeTranscriptSurface(), new MarkdigEntryRenderer(new UrlPolicy()), new FakeUrlLauncher(),
			new ImmediateUiDispatcher(), new SwitchLatencyTracker(NullLogger<SwitchLatencyTracker>.Instance), NullLogger<SessionPaneViewModel>.Instance);
		model.OpenAsync("a", "A").GetAwaiter().GetResult();
		model.ExpandedInputHeight = storedHeight;   // as read from the client state at start (FR.12.36)
		var pane = new SessionPane { DataContext = model };
		pane.Measure(new Size(900, 700));
		pane.Arrange(new Rect(0, 0, 900, 700));
		pane.UpdateLayout();
		return new Fixture(pane, model);
	}

	private static void DoubleClick(UIElement target)
	{
		var args = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent, Source = target };
		typeof(MouseButtonEventArgs).GetProperty(nameof(MouseButtonEventArgs.ClickCount), BindingFlags.Instance | BindingFlags.Public)!.SetValue(args, 2);
		target.RaiseEvent(args);
	}

	private static void SingleClick(UIElement target)
	{
		var args = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent, Source = target };
		typeof(MouseButtonEventArgs).GetProperty(nameof(MouseButtonEventArgs.ClickCount), BindingFlags.Instance | BindingFlags.Public)!.SetValue(args, 1);
		target.RaiseEvent(args);
	}

	private static void Drag(GridSplitter splitter, double vertical) =>
		splitter.RaiseEvent(new DragDeltaEventArgs(0, vertical) { RoutedEvent = Thumb.DragDeltaEvent, Source = splitter });

	private static void DragStart(GridSplitter splitter) =>
		splitter.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent, Source = splitter });

	private static void DragDone(GridSplitter splitter) =>
		splitter.RaiseEvent(new DragCompletedEventArgs(0, 0, canceled: false) { RoutedEvent = Thumb.DragCompletedEvent, Source = splitter });

	[Fact]
	public void TheInputBox_HasAnInnerPaddingOfThreePixels()
	{
		StaTestRunner.Run(() =>
		{
			var f = Create();

			Assert.Equal(new Thickness(3), f.Input.Padding);
		});
	}

	[Fact]
	public void ADoubleClickOnTheSplitter_CollapsesAndExpandsTheInputArea_ASingleClickDoesNot()
	{
		StaTestRunner.Run(() =>
		{
			var f = Create();
			Assert.False(f.Model.IsInputCollapsed);

			SingleClick(f.Splitter);
			Assert.False(f.Model.IsInputCollapsed);

			DoubleClick(f.Splitter);
			Assert.True(f.Model.IsInputCollapsed);

			DoubleClick(f.Splitter);
			Assert.False(f.Model.IsInputCollapsed);
		});
	}

	[Fact]
	public void TheSplitterSitsBetweenTheTranscriptAndTheInputArea_AndIsNamedForAutomation()
	{
		StaTestRunner.Run(() =>
		{
			var f = Create();

			Assert.Equal(3, Grid.GetRow(f.Splitter));
			Assert.Equal("Input area splitter", System.Windows.Automation.AutomationProperties.GetName(f.Splitter));
			Assert.True(f.Splitter.ActualHeight > 0);
		});
	}

	[Fact]
	public void Collapsed_WithTheCommandBarShown_ShowsTheBarAndOneSideButton_NoTextBox()
	{
		StaTestRunner.Run(() =>
		{
			var f = Create();
			f.Model.IsCommandBarVisible = true;
			DoubleClick(f.Splitter);
			f.Settle();

			Assert.Equal(Visibility.Collapsed, f.Input.Visibility);
			Assert.Equal(Visibility.Visible, f.CommandBar.Visibility);
			Assert.Equal(Visibility.Visible, f.Find<Button>("SendButton").Visibility);
			Assert.Equal(Visibility.Collapsed, f.Find<Button>("StopButton").Visibility);
			Assert.Equal(Visibility.Collapsed, f.Find<ToggleButton>("FileModeToggle").Visibility);
			Assert.Equal(Visibility.Collapsed, f.Find<ToggleButton>("SettingsToggle").Visibility);
			Assert.True(f.InputRow.Height.IsAuto);
		});
	}

	[Fact]
	public void Collapsed_WithTheCommandBarHidden_ShowsOneEditableLineOfText()
	{
		StaTestRunner.Run(() =>
		{
			var f = Create();
			f.Model.IsCommandBarVisible = false;
			f.Model.InputText = "a long text that scrolls inside the one line";
			DoubleClick(f.Splitter);
			f.Settle();

			Assert.Equal(Visibility.Visible, f.Input.Visibility);
			Assert.Equal(Visibility.Collapsed, f.CommandBar.Visibility);
			Assert.Equal(TextWrapping.NoWrap, f.Input.TextWrapping);
			Assert.True(f.Input.ActualHeight <= 40, "one line, not " + f.Input.ActualHeight);
			Assert.False(f.Input.IsReadOnly);
			f.Input.Text = "edited in the collapsed line";
			Assert.Equal("edited in the collapsed line", f.Model.InputText);
		});
	}

	[Fact]
	public void Expanded_ShowsTheBoxTheExtrasAndTheSettingsToggle_ThatHidesAndShowsTheCommandBar()
	{
		StaTestRunner.Run(() =>
		{
			var f = Create();
			var settings = f.Find<ToggleButton>("SettingsToggle");
			f.Settle();

			Assert.Equal(Visibility.Visible, f.Input.Visibility);
			Assert.Equal(TextWrapping.Wrap, f.Input.TextWrapping);
			Assert.Equal(Visibility.Visible, settings.Visibility);
			Assert.Equal(Visibility.Visible, f.Find<ToggleButton>("FileModeToggle").Visibility);
			Assert.True(settings.IsChecked);
			Assert.Equal(Visibility.Visible, f.CommandBar.Visibility);

			settings.IsChecked = false;
			f.Settle();

			Assert.False(f.Model.IsCommandBarVisible);
			Assert.Equal(Visibility.Collapsed, f.CommandBar.Visibility);
			Assert.Equal(Visibility.Visible, f.Input.Visibility);
		});
	}

	[Fact]
	public void WhileATurnRuns_StopShows_BesideSendWhenExpanded_AndInsteadOfItWhenCollapsed()
	{
		StaTestRunner.Run(() =>
		{
			var f = Create();
			var send = f.Find<Button>("SendButton");
			var stop = f.Find<Button>("StopButton");
			Assert.Equal(Visibility.Collapsed, stop.Visibility);

			f.Model.IsRunning = true;
			f.Settle();
			Assert.Equal(Visibility.Visible, send.Visibility);
			Assert.Equal(Visibility.Visible, stop.Visibility);

			DoubleClick(f.Splitter);
			f.Settle();
			Assert.Equal(Visibility.Collapsed, send.Visibility);
			Assert.Equal(Visibility.Visible, stop.Visibility);

			f.Model.IsRunning = false;
			f.Settle();
			Assert.Equal(Visibility.Visible, send.Visibility);
			Assert.Equal(Visibility.Collapsed, stop.Visibility);
		});
	}

	[Fact]
	public void TheFocusView_ReplacesTheTranscript_AndTheSwitchHasExactlyOneSideChecked()
	{
		StaTestRunner.Run(() =>
		{
			var f = Create();
			var session = f.Find<RadioButton>("SessionViewButton");
			var focus = f.Find<RadioButton>("FocusViewButton");
			Assert.True(session.IsChecked);
			Assert.False(focus.IsChecked);
			Assert.Equal(Visibility.Visible, f.Find<Grid>("TranscriptHost").Visibility);
			Assert.Equal(Visibility.Collapsed, f.Find<Grid>("FocusHost").Visibility);

			f.Model.SelectFocusViewCommand.Execute(null);
			f.Settle();

			Assert.False(session.IsChecked);
			Assert.True(focus.IsChecked);
			Assert.Equal(Visibility.Collapsed, f.Find<Grid>("TranscriptHost").Visibility);
			Assert.Equal(Visibility.Visible, f.Find<Grid>("FocusHost").Visibility);
			Assert.True(f.Model.IsInputCollapsed);   // and the input area follows the view's default
			Assert.Equal(Visibility.Collapsed, f.Find<Button>("StopButton").Visibility);

			f.Model.SelectSessionViewCommand.Execute(null);
			f.Settle();
			Assert.True(session.IsChecked);
			Assert.False(focus.IsChecked);
			Assert.Equal(Visibility.Visible, f.Find<Grid>("TranscriptHost").Visibility);
			Assert.False(f.Model.IsInputCollapsed);
		});
	}

	[Fact]
	public void TheSwitchButtons_AreNamedForAutomation_AndClickingTheCheckedSideKeepsItChecked()
	{
		StaTestRunner.Run(() =>
		{
			var f = Create();
			var session = f.Find<RadioButton>("SessionViewButton");

			Assert.Equal("Session view", System.Windows.Automation.AutomationProperties.GetName(session));
			Assert.Equal("Focus view", System.Windows.Automation.AutomationProperties.GetName(f.Find<RadioButton>("FocusViewButton")));
			session.Command.Execute(null);
			Assert.True(f.Model.IsSessionSelected);
			Assert.True(session.IsChecked);
		});
	}

	[Fact]
	public void ADragOutOfTheCollapsedState_ExpandsTheAreaFirst()
	{
		StaTestRunner.Run(() =>
		{
			var f = Create();
			DoubleClick(f.Splitter);
			Assert.True(f.Model.IsInputCollapsed);

			Drag(f.Splitter, 0);
			Assert.True(f.Model.IsInputCollapsed);   // no movement, no expansion

			Drag(f.Splitter, -30);
			Assert.False(f.Model.IsInputCollapsed);
		});
	}

	[Fact]
	public void ADraggedHeight_IsFilledByTheTextBox_NotLeftAsBlankSpace()
	{
		StaTestRunner.Run(() =>
		{
			var f = Create();
			f.Settle();
			var natural = f.Input.ActualHeight;

			DragStart(f.Splitter);
			f.InputRow.Height = new GridLength(360);   // what the splitter does while it drags
			f.Settle();
			DragDone(f.Splitter);
			f.Settle();

			Assert.True(f.Input.ActualHeight > natural + 100, $"the text box stayed at {f.Input.ActualHeight} (natural {natural}) in a 360 row");
			Assert.True(f.Input.ActualHeight <= 360);
			Assert.Equal(360, f.Model.ExpandedInputHeight);
		});
	}

	/// <summary>FR.12.36: a height stored by an earlier run sizes the expanded input area from the first layout, and the text box fills it.</summary>
	[Fact]
	public void AStoredHeight_SizesTheInputArea_AtStart()
	{
		StaTestRunner.Run(() =>
		{
			var f = Create(storedHeight: 320);
			f.Settle();

			Assert.False(f.Model.IsInputCollapsed);
			Assert.Equal(320, f.InputRow.ActualHeight, 0.5);
			Assert.True(f.Input.ActualHeight > 150, $"the text box is {f.Input.ActualHeight} high in a 320 row");
		});
	}

	[Fact]
	public void TheTextBoxGrowsAndShrinksWithTheRow_AndTheRowNeverGetsSmallerThanTheButtonsNeed()
	{
		StaTestRunner.Run(() =>
		{
			var f = Create();
			DragStart(f.Splitter);

			f.InputRow.Height = new GridLength(300);
			f.Settle();
			var tall = f.Input.ActualHeight;
			f.InputRow.Height = new GridLength(200);
			f.Settle();
			var medium = f.Input.ActualHeight;
			Assert.True(tall > medium, $"{tall} should be above {medium}");
			Assert.Equal(Visibility.Visible, f.Find<Button>("SendButton").Visibility);

			f.InputRow.Height = new GridLength(30);   // the splitter cannot go below the row's minimum
			f.Settle();
			Assert.True(f.InputRow.ActualHeight >= 130, "the row keeps the room of the buttons: " + f.InputRow.ActualHeight);
			Assert.True(f.Find<Button>("SendButton").ActualHeight >= 32);
			Assert.True(f.Find<ToggleButton>("SettingsToggle").ActualHeight > 0);
		});
	}

	[Fact]
	public void ALongInput_KeepsSendVisible_AndScrollsInsideTheTextBox()
	{
		StaTestRunner.Run(() =>
		{
			foreach (var userSized in new[] { false, true })
			{
				var f = Create();
				f.Model.InputText = string.Join(Environment.NewLine, Enumerable.Range(0, 200).Select(i => "line " + i));
				if (userSized)
				{
					DragStart(f.Splitter);
					f.InputRow.Height = new GridLength(240);
				}

				f.Settle();
				var send = f.Find<Button>("SendButton");
				var origin = send.TranslatePoint(new Point(0, 0), f.Pane);
				Assert.True(send.ActualHeight >= 32, "Send keeps its minimum");
				Assert.True(origin.Y >= 0 && origin.Y + send.ActualHeight <= f.Pane.ActualHeight + 0.5, $"Send is inside the pane (userSized={userSized}): {origin.Y}+{send.ActualHeight} of {f.Pane.ActualHeight}");
				var settings = f.Find<ToggleButton>("SettingsToggle");
				Assert.True(settings.TranslatePoint(new Point(0, settings.ActualHeight), f.Pane).Y <= f.Pane.ActualHeight + 0.5, "Settings is inside the pane");

				var scrolls = Descendants<ScrollViewer>(f.Find<Grid>("InputContent")).Where(s => s.ComputedVerticalScrollBarVisibility == Visibility.Visible).ToList();
				Assert.Single(scrolls);   // only the text box's own
				Assert.True(f.Input.Template.FindName("PART_ContentHost", f.Input) is ScrollViewer own && scrolls.Contains(own));
				Assert.True(f.Input.ActualHeight <= f.Find<Grid>("InputContent").ActualHeight);
			}
		});
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
	public void TheSideButtons_AreIconsInOrder_FileSendStopSchedulesSettings()
	{
		StaTestRunner.Run(() =>
		{
			var f = Create();
			f.Model.IsRunning = true;
			f.Settle();

			var names = new[] { "FileModeToggle", "SendButton", "StopButton", "SettingsToggle" };
			var tops = names.Select(n => f.Find<FrameworkElement>(n).TranslatePoint(new Point(0, 0), f.Pane).Y).ToList();
			Assert.Equal(tops.OrderBy(t => t), tops);   // top to bottom in this order
			Assert.All(names, n => Assert.True(Descendants<TextBlock>(f.Find<FrameworkElement>(n)).All(t => t.Text.Length == 1), n + " shows an icon only"));
			Assert.True(f.Find<Button>("SendButton").ActualHeight > f.Find<Button>("StopButton").ActualHeight, "Send takes the remaining height");
			Assert.Empty(Descendants<ToggleButton>(f.Find<FrameworkElement>("ScheduleButton")).Where(b => b.IsVisible));   // no schedules: no icon
			foreach (var n in new[] { "FileModeToggle", "SendButton", "StopButton", "SettingsToggle" })
				Assert.False(string.IsNullOrEmpty(f.Find<FrameworkElement>(n).ToolTip as string), n + " has a tooltip");
		});
	}

	[Fact]
	public void TheCommandBar_HoldsTheSelectorsAndTheToggles_OnOneLine()
	{
		StaTestRunner.Run(() =>
		{
			var f = Create();
			var shellSlots = new ShellSlots();
			new TurnLoopShellModule().Register(shellSlots, null!);
			new ToolbarShellModule().Register(shellSlots, null!);
			f.CommandBar.Slots = shellSlots;
			f.CommandBar.Populate();
			f.Settle();
			var combo =Descendants<ComboBox>(f.CommandBar).First();
			var toggle = Descendants<ToggleButton>(f.CommandBar).First(t => System.Windows.Automation.AutomationProperties.GetName(t) == "Streaming Session Mode");
			var comboTop = combo.TranslatePoint(new Point(0, 0), f.Pane).Y;
			var toggleTop = toggle.TranslatePoint(new Point(0, 0), f.Pane).Y;
			Assert.True(Math.Abs(comboTop - toggleTop) < 8, $"selectors at {comboTop}, toggles at {toggleTop}");
			Assert.True(Descendants<TextBlock>(toggle).All(t => t.Text.Length == 1), "an icon, no caption");
		});
	}

	[Fact]
	public void ADragThatChangedNothing_LeavesTheNaturalHeightAndItsCap()
	{
		StaTestRunner.Run(() =>
		{
			var f = Create();
			f.Settle();
			var natural = f.Input.ActualHeight;

			DragStart(f.Splitter);
			DragDone(f.Splitter);
			f.Settle();

			Assert.True(f.InputRow.Height.IsAuto);
			Assert.Null(f.Model.ExpandedInputHeight);
			Assert.Equal(natural, f.Input.ActualHeight, 1);
			f.Model.InputText = string.Join(Environment.NewLine, Enumerable.Range(0, 60).Select(i => "line " + i));
			f.Settle();
			Assert.True(f.Find<DockPanel>("InputBody").ActualHeight <= 262, "the natural height stays capped");
		});
	}

	[Fact]
	public void TheFirstDragOutOfTheCollapsedState_DoesNotResetTheRowTheSplitterIsSizing()
	{
		StaTestRunner.Run(() =>
		{
			var f = Create();
			f.Model.ExpandedInputHeight = 400;   // an older, taller height must not jump in mid-drag
			DoubleClick(f.Splitter);
			f.Settle();

			DragStart(f.Splitter);
			f.InputRow.Height = new GridLength(90);   // the splitter sizing the row from the collapsed height
			Drag(f.Splitter, -30);

			Assert.False(f.Model.IsInputCollapsed);
			Assert.False(f.InputRow.Height.IsAuto);                  // not reset to the natural height
			Assert.True(f.InputRow.Height.Value < 200, "the remembered 400 must not jump in: " + f.InputRow.Height.Value);   // the real splitter moved the row from 90
		});
	}

	[Fact]
	public void TheHeightTheUserDraggedTo_IsRememberedAcrossACollapse()
	{
		StaTestRunner.Run(() =>
		{
			var f = Create();
			f.InputRow.Height = new GridLength(220);   // what the splitter does while it drags
			f.Settle();
			DragDone(f.Splitter);
			Assert.Equal(220, f.Model.ExpandedInputHeight);

			DoubleClick(f.Splitter);
			f.Settle();
			Assert.True(f.InputRow.Height.IsAuto);
			Assert.Equal(220, f.Model.ExpandedInputHeight);

			DoubleClick(f.Splitter);
			f.Settle();
			Assert.Equal(220, f.InputRow.Height.Value);
			Assert.False(f.InputRow.Height.IsAuto);
		});
	}

	[Fact]
	public void TheInputRowHasTheNaturalHeightUntilTheUserResizesIt()
	{
		StaTestRunner.Run(() =>
		{
			var f = Create();

			Assert.True(f.InputRow.Height.IsAuto);
			Assert.Null(f.Model.ExpandedInputHeight);
			DoubleClick(f.Splitter);
			DoubleClick(f.Splitter);
			Assert.True(f.InputRow.Height.IsAuto);
		});
	}

	[Fact]
	public void TheSidePane_StaysForPreviews_AndTheFocusViewHasNoPlaceInIt()
	{
		StaTestRunner.Run(() =>
		{
			var f = Create();

			Assert.Equal(Visibility.Collapsed, f.Find<Grid>("SidePane").Visibility);
			f.Model.SelectFocusViewCommand.Execute(null);
			f.Settle();
			Assert.Equal(Visibility.Collapsed, f.Find<Grid>("SidePane").Visibility);   // Focus no longer opens the side column
		});
	}
}
