using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.WPF.Infrastructure;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.State;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.ViewModels;
using TessynDesktop.WPF.Views;

namespace TessynDesktop.WPF.Tests;

/// <summary>The fixes after the QA review of W10a (report 469): screensaver over the web pages, mouse activity, hook clean-up, frame metrics, coalescing, Send's focus.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class ChromeReviewFixTests
{
	private static readonly DateTime T0 = new(2026, 11, 16, 9, 0, 0, DateTimeKind.Utc);

	private sealed class FixedProbe : ISystemThemeProbe
	{
		public bool IsLight => true;

		public event EventHandler? Changed
		{
			add { }
			remove { }
		}
	}

	private sealed class OneImage : IScreensaverImages
	{
		public IReadOnlyList<string> List(string directory) => ["a.png"];
	}

	private static (ClientSettingsStore Store, ScreensaverController Controller) NewController(int timeoutSeconds = 5)
	{
		var store = new ClientSettingsStore(new MemoryClientState(), NullLogger<ClientSettingsStore>.Instance);
		store.Update<ScreensaverSettings>(s =>
		{
			s.Directory = @"D:\pics";
			s.TimeoutSeconds = timeoutSeconds;
		});
		return (store, new ScreensaverController(store, new OneImage(), T0));
	}

	private static MouseButtonEventArgs ClickEvent() => new(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseDownEvent };

	// H1 ---------------------------------------------------------------------------------------------------------------------------------------------

	[Fact]
	public void TheCover_HidesTheContentUnderIt_AndThereforeTheWebView2Windows_WhileItIsUp()
	{
		StaTestRunner.Run(() =>
		{
			var (_, controller) = NewController();
			var content = new Border();
			var cover = new ScreensaverOverlay { Covered = content };
			cover.Attach(controller, clock: () => T0);

			controller.Tick(T0.AddSeconds(5));
			cover.Refresh();

			Assert.Equal(Visibility.Visible, cover.Visibility);
			Assert.Equal(Visibility.Hidden, content.Visibility);   // a child window would otherwise paint over the cover

			Assert.True(cover.HandleInput(ClickEvent()));   // a click lands on the cover and ends it
			cover.Refresh();

			Assert.Equal(Visibility.Collapsed, cover.Visibility);
			Assert.Equal(Visibility.Visible, content.Visibility);
			cover.Detach();
		});
	}

	[Fact]
	public void AKeyPress_EndsTheCover_AndIsSwallowed()
	{
		StaTestRunner.Run(() =>
		{
			var (_, controller) = NewController();
			var content = new Border();
			var cover = new ScreensaverOverlay { Covered = content };
			cover.Attach(controller, clock: () => T0);
			controller.Tick(T0.AddSeconds(5));
			cover.Refresh();

			using var source = new HwndSource(new HwndSourceParameters("w10a-test") { Width = 10, Height = 10 });
			var key = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.A) { RoutedEvent = Keyboard.PreviewKeyDownEvent };

			Assert.True(cover.HandleInput(key));
			cover.Refresh();
			Assert.Equal(Visibility.Visible, content.Visibility);
			cover.Detach();
		});
	}

	[Fact]
	public void ActivityInAWebPage_PostponesTheStart_ButNeverEndsARunningScreensaver()
	{
		var (_, controller) = NewController(timeoutSeconds: 60);

		controller.NotePageActivity(T0.AddSeconds(50));   // the page saw a wheel turn
		controller.Tick(T0.AddSeconds(60));
		Assert.False(controller.IsActive);   // 10 s since the last activity, not 60

		controller.Tick(T0.AddSeconds(110));
		Assert.True(controller.IsActive);

		controller.NotePageActivity(T0.AddSeconds(111));   // a message that was already on its way
		Assert.True(controller.IsActive);
	}

	[Fact]
	public void TheWebPages_ReportPointerWheelAndKeyActivity()
	{
		foreach (var page in new[] { Constants.Paths.TranscriptPageFileName, Constants.Paths.PreviewPageFileName })
		{
			var script = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, Constants.Paths.WebAssetsFolder, Path.ChangeExtension(page, ".js")));

			Assert.Contains("ev: 'activity'", script);
			Assert.Contains("'pointermove'", script);
			foreach (var type in new[] { "pointerdown", "wheel", "keydown" })
				Assert.Contains($"'{type}'", script);
		}
	}

	// H2 ---------------------------------------------------------------------------------------------------------------------------------------------

	[Fact]
	public void MovementThreshold_IgnoresJitter_AndCountsARealMove()
	{
		var threshold = new MovementThreshold(6);

		Assert.False(threshold.IsMovement(new Point(100, 100)));   // the first position only sets the start
		Assert.False(threshold.IsMovement(new Point(102, 101)));
		Assert.False(threshold.IsMovement(new Point(103, 103)));   // still within 6 px of the start: jitter does not creep away
		Assert.True(threshold.IsMovement(new Point(110, 100)));
		Assert.False(threshold.IsMovement(new Point(111, 100)));   // measured from the new anchor
	}

	[Fact]
	public void TheCover_EndsOnARealMouseMove_ButNotOnJitter()
	{
		StaTestRunner.Run(() =>
		{
			var (_, controller) = NewController();
			var cover = new ScreensaverOverlay { Covered = new Border() };
			cover.Attach(controller, clock: () => T0);
			cover.NoteMouseMove(new Point(50, 50));
			controller.Tick(T0.AddSeconds(5));
			Assert.True(controller.IsActive);

			cover.NoteMouseMove(new Point(52, 51));
			Assert.True(controller.IsActive);   // a resting hand does not wake the screen

			cover.NoteMouseMove(new Point(90, 80));
			Assert.False(controller.IsActive);
			cover.Detach();
		});
	}

	[Fact]
	public void AMouseMove_PostponesTheStart_WhileTheUserReads()
	{
		StaTestRunner.Run(() =>
		{
			var (_, controller) = NewController(timeoutSeconds: 60);
			var now = T0;
			var cover = new ScreensaverOverlay { Covered = new Border() };
			cover.Attach(controller, clock: () => now);
			cover.NoteMouseMove(new Point(0, 0));

			now = T0.AddSeconds(50);
			cover.NoteMouseMove(new Point(40, 40));
			controller.Tick(T0.AddSeconds(70));

			Assert.False(controller.IsActive);
			cover.Detach();
		});
	}

	// M3 ---------------------------------------------------------------------------------------------------------------------------------------------

	[Fact]
	public void Detach_RemovesTheStaticInputHook_AndTheControllerSubscription()
	{
		StaTestRunner.Run(() =>
		{
			var (_, controller) = NewController();
			var reads = 0;
			var cover = new ScreensaverOverlay { Covered = new Border() };
			cover.Attach(controller, clock: () =>
			{
				reads++;
				return T0;
			});

			InputManager.Current.ProcessInput(ClickEvent());
			Assert.True(reads > 0, "the hook must see the input while attached");

			cover.Detach();
			Assert.False(cover.IsAttached);
			reads = 0;
			InputManager.Current.ProcessInput(ClickEvent());
			Assert.Equal(0, reads);   // a disposed window's overlay no longer counts or cancels input

			controller.Tick(T0.AddSeconds(5));
			Assert.Equal(Visibility.Collapsed, cover.Visibility);   // nor does it follow the controller any more
		});
	}

	[Fact]
	public void Attach_Twice_LeavesOneHook()
	{
		StaTestRunner.Run(() =>
		{
			var (_, controller) = NewController();
			var reads = 0;
			var cover = new ScreensaverOverlay { Covered = new Border() };
			Func<DateTime> clock = () =>
			{
				reads++;
				return T0;
			};
			cover.Attach(controller, clock: clock);
			cover.Attach(controller, clock: clock);

			reads = 0;
			InputManager.Current.ProcessInput(ClickEvent());

			Assert.True(reads is > 0 and <= 2, $"one hook reads the clock once per staged input, got {reads}");
			cover.Detach();
		});
	}

	// M4 ---------------------------------------------------------------------------------------------------------------------------------------------

	[Fact]
	public void FrameMetrics_FollowTheDpiOfTheMonitor_NotOnlyThePrimaryOne()
	{
		var at96 = FrameMetrics.FramePixels(96);
		var at192 = FrameMetrics.FramePixels(192);

		Assert.True(at96.X > 0 && at96.Y > 0);
		Assert.True(at192.X > at96.X, $"a 200 % monitor has a thicker frame in pixels ({at192.X} vs {at96.X})");

		// What the old code did: the 96-dpi frame taken as DIPs on every monitor. On a 200 % monitor that is too wide a margin (a gap at the edges).
		var margin = FrameMetrics.MaximizedMargin(2.0);
		Assert.Equal(at192.X / 2.0, margin.Left);
		Assert.Equal(at192.Y / 2.0, margin.Top);
		Assert.NotEqual(at96.X / 1.0, margin.Left);
		Assert.Equal(margin.Left, margin.Right);
	}

	[Fact]
	public void FrameMetrics_FromPixels_ConvertsToDips()
	{
		var thickness = FrameMetrics.FromPixels(16, 12, 2.0);

		Assert.Equal(new Thickness(8, 6, 8, 6), thickness);
	}

	// M5 ---------------------------------------------------------------------------------------------------------------------------------------------

	[Fact]
	public void UiCoalescer_FoldsAWheelSpinIntoOneRun()
	{
		StaTestRunner.Run(() =>
		{
			var runs = 0;
			var coalescer = new UiCoalescer(() => runs++);

			for (var notch = 0; notch < 30; notch++)
				coalescer.Request(Dispatcher.CurrentDispatcher);

			Assert.Equal(0, runs);
			DrainDispatcher();
			Assert.Equal(1, runs);

			coalescer.Request(Dispatcher.CurrentDispatcher);   // a later spin runs again
			DrainDispatcher();
			Assert.Equal(2, runs);
		});
	}

	[Fact]
	public void TheApplier_KeepsTheThemeDictionary_WhenOnlyAFontChanged_AndReplacesItWhenTheThemeChanged()
	{
		StaTestRunner.Run(() =>
		{
			var store = new ClientSettingsStore(new MemoryClientState(), NullLogger<ClientSettingsStore>.Instance);
			var appearance = new AppearanceService(store, new FixedProbe());
			var applier = new AppearanceApplier(appearance, new ImmediateUiDispatcher());
			var resources = new ResourceDictionary();

			applier.Apply(resources);
			var first = Assert.Single(resources.MergedDictionaries);

			appearance.AdjustFont(FontTarget.Input, +2);
			applier.Apply(resources);

			Assert.Same(first, Assert.Single(resources.MergedDictionaries));   // not torn down for a font size
			Assert.Equal(15.0, resources[Constants.Appearance.InputFontSizeKey]);

			appearance.ToggleTheme();
			applier.Apply(resources);

			Assert.NotSame(first, Assert.Single(resources.MergedDictionaries));
		});
	}

	[Fact]
	public void ThePageIgnoresAnAppearanceMessage_ThatChangesNoFontSize_WithoutMeasuringAgain()
	{
		var script = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, Constants.Paths.WebAssetsFolder, "transcript.js"));

		Assert.Contains("sizeChanged", script);
		Assert.Contains("if (S && sizeChanged) remeasure()", script);
	}

	// M6 ---------------------------------------------------------------------------------------------------------------------------------------------

	[Fact]
	public void TheSystemRow_FollowsTheFontSizeTheWheelChanges()
	{
		var html = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, Constants.Paths.WebAssetsFolder, Constants.Paths.TranscriptPageFileName));
		var rule = html.Split('\n').Single(l => l.StartsWith(".e.S .c{", StringComparison.Ordinal));

		Assert.Contains("var(--fs-a)", rule);   // the wheel over a system row changes --fs-a (the "assistant" target)
		Assert.DoesNotContain("font-size:12px", rule);
	}

	// M7 ---------------------------------------------------------------------------------------------------------------------------------------------

	[Fact]
	public void TheImageList_LeavesOutWebp_WhichWpfCannotDecode()
	{
		var dir = Path.Combine(Path.GetTempPath(), "tessyn-ss-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(dir);
		try
		{
			foreach (var name in new[] { "a.png", "b.JPG", "c.webp", "d.gif", "e.txt" })
				File.WriteAllBytes(Path.Combine(dir, name), [0]);

			var listed = new FolderScreensaverImages().List(dir).Select(Path.GetFileName).ToArray();

			Assert.Equal(["a.png", "b.JPG", "d.gif"], listed);
		}
		finally
		{
			TestDirectory.Delete(dir);
		}
	}

	// M8 ---------------------------------------------------------------------------------------------------------------------------------------------

	[Fact]
	public void SendFocusPolicy_AllowsThePromptBoxAndNonTextElements_ButNotTheFindBoxOrTheSuggestions()
	{
		StaTestRunner.Run(() =>
		{
			var popup = new AutocompletePopup();
			popup.Measure(new Size(300, 300));
			var inside = Descendants(popup).First(d => d is not AutocompletePopup);

			Assert.True(SendFocusPolicy.Allows(new TextBox { Name = Constants.Appearance.PromptBoxName }));
			Assert.True(SendFocusPolicy.Allows(new Button()));
			Assert.True(SendFocusPolicy.Allows(null));
			Assert.False(SendFocusPolicy.Allows(new TextBox { Name = "FindBox" }));
			Assert.False(SendFocusPolicy.Allows(popup));
			Assert.False(SendFocusPolicy.Allows(inside));
		});
	}

	[Fact]
	public void TheSendCommand_UsesThatPolicy_SoCtrlEnterInTheFindBoxDoesNotSend()
	{
		var store = new ClientSettingsStore(new MemoryClientState(), NullLogger<ClientSettingsStore>.Instance);
		var commands = new ShellCommands(store, NullLogger<ShellCommands>.Instance);
		var appearance = new AppearanceService(store, new FixedProbe());
		var ui = new ImmediateUiDispatcher();
		var connection = new ConnectionViewModel(new ScriptedLocator(), new FixedClientFactory(new FakeTessynClient()), ui, NullLogger<ConnectionViewModel>.Instance);
		var main = new MainViewModel(connection, _ => throw new InvalidOperationException(), _ => throw new InvalidOperationException(), new MemoryClientState(), NullLogger<MainViewModel>.Instance);
		var bar = new StatusBarViewModel(connection, main, ui, NullLogger<StatusBarViewModel>.Instance);
		new ChromeShellModule(bar, new TitleBarViewModel(appearance, store, commands, new FakeShellLayout(), ui, "1", false), main, appearance).Register(new ShellSlots(), commands);

		var send = commands.Find(Constants.Appearance.SendCommandId)!;

		Assert.NotNull(send.AllowsFocus);
		StaTestRunner.Run(() =>
		{
			Assert.False(send.AllowsFocus!(new TextBox { Name = "FindBox" }));
			Assert.True(send.AllowsFocus!(new TextBox { Name = Constants.Appearance.PromptBoxName }));
		});
	}

	[Fact]
	public void TryExecuteGesture_LeavesTheKeyAlone_WhenTheCommandRefusesTheFocusedElement()
	{
		var store = new ClientSettingsStore(new MemoryClientState(), NullLogger<ClientSettingsStore>.Instance);
		var commands = new ShellCommands(store, NullLogger<ShellCommands>.Instance);
		var ran = 0;
		commands.Register(new ShellCommand
		{
			Id = "test.send",
			Title = "Send",
			DefaultGesture = "Ctrl+Enter",
			AllowInTextInput = true,
			AllowsFocus = focused => focused is not "find",
			ExecuteAsync = _ =>
			{
				ran++;
				return Task.CompletedTask;
			},
		});

		Assert.False(commands.TryExecuteGesture(Key.Enter, ModifierKeys.Control, textInputFocused: true, focused: "find"));
		Assert.Equal(0, ran);

		Assert.True(commands.TryExecuteGesture(Key.Enter, ModifierKeys.Control, textInputFocused: true, focused: "prompt"));
		Assert.Equal(1, ran);
	}

	private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
	{
		yield return root;
		foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
			foreach (var nested in Descendants(child))
				yield return nested;
	}

	private static void DrainDispatcher()
	{
		var frame = new DispatcherFrame();
		Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
		Dispatcher.PushFrame(frame);
	}
}
