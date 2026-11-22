using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.ViewModels;
using TessynDesktop.WPF.Views;
using static TessynDesktop.WPF.Tests.ElevatedRunFixture;

namespace TessynDesktop.WPF.Tests;

/// <summary>TC.ER.51 and TC.ER.52 (view part): the elevated-run card in a real layout, its text controls, its buttons and the deliberate-click rules (FR.23.32).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class ElevatedRunCardViewTests
{
	private const double PanelWidth = 420;

	private readonly ElevatedRunFixture _f = new();

	private static string ThirtyLines => string.Join("\n", Enumerable.Range(1, 30).Select(i => $"echo line {i}"));

	private static List<T> Descendants<T>(DependencyObject root) where T : DependencyObject
	{
		var result = new List<T>();
		for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
		{
			var child = VisualTreeHelper.GetChild(root, i);
			if (child is T match)
				result.Add(match);
			result.AddRange(Descendants<T>(child));
		}

		return result;
	}

	private static T Named<T>(DependencyObject root, string name) where T : FrameworkElement =>
		Descendants<T>(root).Single(e => AutomationProperties.GetName(e) == name);

	/// <summary>A shown (off-screen, never activated) window with a message box above the Focus panel, so focus behaviour is real.</summary>
	private sealed class Shown(FocusViewModel focus)
	{
		public TextBox Message { get; } = new() { Name = "Message" };
		public FocusPanel Panel { get; } = new() { DataContext = focus };
		public Window Window { get; private set; } = null!;

		public Shown Show()
		{
			var stack = new DockPanel();
			DockPanel.SetDock(Message, Dock.Top);
			stack.Children.Add(Message);
			stack.Children.Add(Panel);
			Window = new Window { Content = stack, Width = PanelWidth, Height = 900, Left = -3000, Top = -3000, ShowActivated = false, ShowInTaskbar = false };
			Window.Show();
			Layout();
			return this;
		}

		public void Layout() => Panel.UpdateLayout();

		public KeyEventArgs Key(UIElement target, Key key, ModifierKeys modifiers = ModifierKeys.None)
		{
			var source = PresentationSource.FromVisual(target)!;
			var args = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key) { RoutedEvent = Keyboard.KeyDownEvent };
			target.RaiseEvent(args);
			return args;
		}
	}

	// ── TC.ER.51 ──────────────────────────────────────────────────────────────

	[Fact]
	public void TC_ER_51_TheCommandBlock_IsVerbatimText_ScrollsBothWays_AndNothingInTheCardIsMarkedAsAPath()
	{
		string commandText = "", reasonText = "", dirText = "", headerText = "";
		var markedPaths = -1;
		var wrap = TextWrapping.Wrap;
		var maxLines = 0;
		ScrollBarVisibility horizontal = default, vertical = default;
		double scrollableWidth = 0, scrollableHeight = 0, viewportHeight = 0, lineHeight = 0;
		var longScrollHeight = 0d;
		bool readOnly = false, ctrlClickOpens = true;
		StaTestRunner.Run(() =>
		{
			var awkward = ElevatedRunCardViewModelTests.AwkwardCommand;
			var (focus, _) = _f.ShowAsync(Run(command: awkward)).GetAwaiter().GetResult();
			using (focus)
			{
				var shown = new Shown(focus).Show();
				var box = Named<TextBox>(shown.Panel, "Command");
				commandText = box.Text;
				readOnly = box.IsReadOnly;
				wrap = box.TextWrapping;
				maxLines = box.MaxLines;
				horizontal = box.HorizontalScrollBarVisibility;
				vertical = box.VerticalScrollBarVisibility;
				reasonText = Named<TextBox>(shown.Panel, "Reason").Text;
				dirText = Named<TextBox>(shown.Panel, "Working directory").Text;

				var scroller = (ScrollViewer)box.Template.FindName("PART_ContentHost", box);
				scrollableWidth = scroller.ScrollableWidth;          // the 400-character line scrolls sideways
				lineHeight = box.FontSize * box.FontFamily.LineSpacing;

				var card = Descendants<FocusCardText>(shown.Panel).Single();
				headerText = card.PlainText;
				markedPaths = card.MarkedPaths.Count;                // the title holds C:\Windows\notepad.exe: not marked
				ctrlClickOpens = card.TryActivatePathAt(new Point(20, 5), ModifierKeys.Control);
				shown.Window.Close();
			}

			var (longFocus, _) = new ElevatedRunFixture().ShowAsync(Run(command: ThirtyLines)).GetAwaiter().GetResult();
			using (longFocus)
			{
				var shown = new Shown(longFocus).Show();
				var box = Named<TextBox>(shown.Panel, "Command");
				var scroller = (ScrollViewer)box.Template.FindName("PART_ContentHost", box);
				scrollableHeight = scroller.ScrollableHeight;        // 30 lines scroll vertically
				viewportHeight = scroller.ViewportHeight;
				longScrollHeight = box.ActualHeight;
				shown.Window.Close();
			}
		});

		Assert.Equal(ElevatedRunCardViewModelTests.AwkwardCommand, commandText);   // unchanged: no markup parsed, no ellipsis, no line-ending change
		Assert.True(readOnly);
		Assert.Equal(TextWrapping.NoWrap, wrap);
		Assert.Equal(12, maxLines);
		Assert.Equal(ScrollBarVisibility.Auto, horizontal);
		Assert.Equal(ScrollBarVisibility.Auto, vertical);
		Assert.Equal("Run the suite as the test user", reasonText);
		Assert.Equal(@"C:\Projects\Personal\Tessyn-wt-elevruns", dirText);
		Assert.True(scrollableWidth > 0);
		Assert.True(scrollableHeight > 0);
		Assert.True(viewportHeight > 0 && viewportHeight <= lineHeight * 12 + 20, $"at most 12 lines tall, was {viewportHeight} for a line of {lineHeight}");
		Assert.True(longScrollHeight > 0);
		Assert.Equal(0, markedPaths);
		Assert.False(ctrlClickOpens);
		Assert.Contains("C:\\Windows\\notepad.exe", headerText);                   // shown as plain text in the title
	}

	[Fact]
	public void TC_ER_51_TheCommandAndTheFolder_AreReadByUiAutomationUnderTheirNames()
	{
		var names = new List<string>();
		string commandValue = "";
		StaTestRunner.Run(() =>
		{
			var (focus, _) = _f.ShowAsync(Run(command: "cmd /c echo hi & exit 3")).GetAwaiter().GetResult();
			using (focus)
			{
				var shown = new Shown(focus).Show();
				foreach (var name in new[] { "Reason", "Working directory", "Command" })
				{
					var element = Descendants<TextBox>(shown.Panel).Single(e => AutomationProperties.GetName(e) == name);
					names.Add(UIElementAutomationPeer.CreatePeerForElement(element)!.GetName());
				}

				var box = Named<TextBox>(shown.Panel, "Command");
				commandValue = ((System.Windows.Automation.Provider.IValueProvider)UIElementAutomationPeer.CreatePeerForElement(box)!.GetPattern(PatternInterface.Value)).Value;
				shown.Window.Close();
			}
		});

		Assert.Equal(["Reason", "Working directory", "Command"], names);
		Assert.Equal("cmd /c echo hi & exit 3", commandValue);
	}

	// ── TC.ER.52 ──────────────────────────────────────────────────────────────

	[Fact]
	public void TC_ER_52_RunAsAdministrator_IsNotTheDefaultButton_HasNoAccessKey_NoShortcut_AndTheThreeNamesAreRight()
	{
		bool isDefault = true, hasAccessKey = true;
		int buttonInputBindings = -1, panelBindings = -1, windowBindings = -1, buttonCommandBindings = -1;
		var names = new List<string>();
		object? content = null;
		StaTestRunner.Run(() =>
		{
			var run = Run(ElevatedRunStatus.Pending);
			var (focus, _) = _f.ShowAsync(run).GetAwaiter().GetResult();
			using (focus)
			{
				var shown = new Shown(focus).Show();
				var button = Named<Button>(shown.Panel, "Run as administrator");
				isDefault = button.IsDefault;
				content = button.Content;
				hasAccessKey = button.Content is string text && text.Contains('_') || Descendants<AccessText>(button).Any();
				buttonInputBindings = button.InputBindings.Count;
				buttonCommandBindings = button.CommandBindings.Count;
				panelBindings = shown.Panel.InputBindings.Count;
				windowBindings = shown.Window.InputBindings.Count;
				names.Add(UIElementAutomationPeer.CreatePeerForElement(button)!.GetName());
				names.Add(UIElementAutomationPeer.CreatePeerForElement(Named<Button>(shown.Panel, "Decline"))!.GetName());

				_f.Client.SetElevatedStatus("er-1", ElevatedRunStatus.Running, r => r with { StartedUtc = T0 });
				names.Add(UIElementAutomationPeer.CreatePeerForElement(Named<Button>(shown.Panel, "Stop"))!.GetName());
				shown.Window.Close();
			}
		});

		Assert.False(isDefault);
		Assert.Equal("Run as administrator", content);
		Assert.False(hasAccessKey);
		Assert.Equal(0, buttonInputBindings);
		Assert.Equal(0, buttonCommandBindings);
		Assert.Equal(0, panelBindings);
		Assert.Equal(0, windowBindings);
		Assert.Equal(["Run as administrator", "Decline", "Stop"], names);
	}

	[Fact]
	public void TC_ER_52_EnterAndCtrlEnter_NeverApprove_NotInTheNoteBox_NotAnywhereInThePanel_AndSendAnswersDoesNot()
	{
		StaTestRunner.Run(() =>
		{
			var (focus, _) = _f.ShowAsync(Run()).GetAwaiter().GetResult();
			using (focus)
			{
				var shown = new Shown(focus).Show();
				var note = Named<TextBox>(shown.Panel, "Note for the agent (optional)");
				note.Text = "careful";

				shown.Key(note, Key.Enter);
				shown.Key(note, Key.Enter, ModifierKeys.Control);
				var handled = shown.Panel.HandleReplyKey(note, Key.Enter, ModifierKeys.Control);     // the Send answers shortcut (FR.23.22)
				shown.Key(shown.Message, Key.Enter);
				shown.Key(shown.Panel, Key.Enter);
				shown.Key(shown.Window, Key.Enter, ModifierKeys.Control);

				Assert.True(handled);                                  // Ctrl+Enter is Send answers and nothing else
				Assert.Empty(_f.Client.ElevatedApproved);
				Assert.Empty(_f.Launcher.Launches);
				Assert.Equal("careful", note.Text);                    // Enter did not even leave the note box a newline (single line)
				shown.Window.Close();
			}
		});
	}

	[Fact]
	public void TC_ER_52_TheCardNeverTakesFocus_WhenItAppears_OrWhenItRefreshes()
	{
		IInputElement? afterAppear = null, afterRefresh = null, afterRunning = null, message = null;
		StaTestRunner.Run(() =>
		{
			var run = Run();
			_f.Client.PutElevatedRun(run);
			using var focus = _f.CreateFocus();
			var shown = new Shown(focus).Show();
			shown.Message.Focus();
			message = FocusManager.GetFocusedElement(shown.Window);

			_f.Client.Attention[Lead] = [Item(run)];                  // the card appears
			focus.SetSessionAsync(Lead).GetAwaiter().GetResult();
			shown.Layout();
			afterAppear = FocusManager.GetFocusedElement(shown.Window);

			focus.RefreshAsync().GetAwaiter().GetResult();            // a refresh
			_f.Client.SetElevatedStatus("er-1", ElevatedRunStatus.Launching);
			shown.Layout();
			afterRefresh = FocusManager.GetFocusedElement(shown.Window);

			_f.Client.SetElevatedStatus("er-1", ElevatedRunStatus.Running, r => r with { StartedUtc = T0 });
			shown.Layout();
			afterRunning = FocusManager.GetFocusedElement(shown.Window);
			shown.Window.Close();
		});

		Assert.NotNull(message);
		Assert.Same(message, afterAppear);
		Assert.Same(message, afterRefresh);
		Assert.Same(message, afterRunning);
	}

	[Fact]
	public void TC_ER_52_EnterOnTheButtonAfterTab_Approves_ButOnlyOnTheButton()
	{
		StaTestRunner.Run(() =>
		{
			var (focus, card) = _f.ShowAsync(Run()).GetAwaiter().GetResult();
			using (focus)
			{
				var shown = new Shown(focus).Show();
				var button = Named<Button>(shown.Panel, "Run as administrator");

				button.Focus();                                        // the User tabbed to it
				shown.Key(button, Key.Enter);
				WaitFor(() => _f.Client.ElevatedApproved.Count > 0);

				Assert.Equal(["er-1"], _f.Client.ElevatedApproved);
				Assert.Single(_f.Launcher.Launches);
				Assert.True(card.IsLaunching || card.IsRunning || card.IsFinal || _f.Client.ElevatedLaunched.Count > 0);
				shown.Window.Close();
			}
		});
	}

	private static void WaitFor(Func<bool> condition)
	{
		var deadline = DateTime.UtcNow.AddSeconds(5);
		while (!condition() && DateTime.UtcNow < deadline)
			System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
	}
}
