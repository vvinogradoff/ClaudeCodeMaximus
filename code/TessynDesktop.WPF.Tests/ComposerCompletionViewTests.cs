using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Infrastructure;
using TessynDesktop.WPF.Rendering;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.ViewModels;
using TessynDesktop.WPF.Views;

namespace TessynDesktop.WPF.Tests;

/// <summary>The real session pane on an STA thread (W1b): the suggestion popup follows typing and the keyboard, the pane's own text changes never open it, and the sign-in notice follows the flag.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class ComposerCompletionViewTests
{
	private static (SessionPane Pane, TextBox Input, Popup Popup, SessionPaneViewModel Model, FakeTessynClient Client) Create()
	{
		var client = new FakeTessynClient();
		client.Sessions["a"] = FakeSession.WithEntries(3);
		client.AutocompleteAnswer = new(RemoteProtocol.Composer.AutocompleteModes.Symbol, 4, 3, true,
		[
			new AutocompleteItemDto("Foo", "Ns.Foo", "class", "Ns", 0, 2),
			new AutocompleteItemDto("FooBar", "Ns.FooBar", "class", "Ns", 0, 2),
		]);
		var model = new SessionPaneViewModel(client, new FakeTranscriptSurface(), new MarkdigEntryRenderer(new UrlPolicy()), new FakeUrlLauncher(),
			new ImmediateUiDispatcher(), new SwitchLatencyTracker(NullLogger<SwitchLatencyTracker>.Instance), NullLogger<SessionPaneViewModel>.Instance);
		model.Autocomplete.Debounce = TimeSpan.Zero;
		model.DraftSync.Delay = TimeSpan.FromMinutes(5);
		model.OpenAsync("a", "A").GetAwaiter().GetResult();
		var pane = new SessionPane { DataContext = model };
		pane.Measure(new Size(900, 700));
		pane.Arrange(new Rect(0, 0, 900, 700));
		pane.UpdateLayout();
		return (pane, (TextBox)pane.FindName("InputBox"), (Popup)pane.FindName("SuggestionsPopup"), model, client);
	}

	/// <summary>Runs the queued bindings: a test thread has no message loop, and XAML bindings attach at data-bind priority.</summary>
	private static void Pump() => System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);

	private static void Type(TextBox input, string text)
	{
		input.Text = text;
		input.CaretIndex = text.Length;
	}

	private static KeyEventArgs Key(UIElement target, Key key)
	{
		var source = new HwndSource(0, 0, 0, 0, 0, "w1b-test", IntPtr.Zero);
		var args = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
		target.RaiseEvent(args);
		return args;
	}

	[Fact]
	public void Typing_a_hash_opens_the_popup_with_the_first_row_selected()
	{
		StaTestRunner.Run(() =>
		{
			var (_, input, _, model, _) = Create();

			Type(input, "see #Fo");

			Assert.True(model.Autocomplete.IsOpen);
			Assert.True(model.Autocomplete.IsOpen);
			Assert.Equal(2, model.Autocomplete.Items.Count);
			Assert.Equal(0, model.Autocomplete.SelectedIndex);
		});
	}

	[Fact]
	public void Plain_typing_does_not_ask_the_daemon()
	{
		StaTestRunner.Run(() =>
		{
			var (_, input, _, model, client) = Create();

			Type(input, "just words");

			Assert.False(model.Autocomplete.IsOpen);
			Assert.DoesNotContain(client.Calls, c => c.StartsWith("autocomplete.suggest:", StringComparison.Ordinal));
		});
	}

	[Fact]
	public void Down_then_Enter_accepts_the_second_row_and_puts_the_caret_behind_it()
	{
		StaTestRunner.Run(() =>
		{
			var (_, input, _, model, _) = Create();
			Type(input, "see #Fo");

			var down = Key(input, System.Windows.Input.Key.Down);
			var enter = Key(input, System.Windows.Input.Key.Enter);

			Assert.True(down.Handled);
			Assert.True(enter.Handled);
			Assert.Equal("see Ns.FooBar", input.Text);
			Assert.Equal("see Ns.FooBar", model.InputText);
			Assert.Equal("see Ns.FooBar".Length, input.CaretIndex);
			Assert.False(model.Autocomplete.IsOpen);
		});
	}

	[Fact]
	public void Tab_accepts_the_selected_row()
	{
		StaTestRunner.Run(() =>
		{
			var (_, input, _, _, _) = Create();
			Type(input, "see #Fo");

			var tab = Key(input, System.Windows.Input.Key.Tab);

			Assert.True(tab.Handled);
			Assert.Equal("see Ns.Foo", input.Text);
		});
	}

	[Fact]
	public void Escape_closes_the_popup_and_keeps_the_text()
	{
		StaTestRunner.Run(() =>
		{
			var (_, input, _, model, _) = Create();
			Type(input, "see #Fo");

			var escape = Key(input, System.Windows.Input.Key.Escape);

			Assert.True(escape.Handled);
			Assert.False(model.Autocomplete.IsOpen);
			Assert.Equal("see #Fo", input.Text);
		});
	}

	[Fact]
	public void With_the_popup_closed_Enter_and_the_arrows_are_left_to_the_text_box()
	{
		StaTestRunner.Run(() =>
		{
			var (_, input, _, _, _) = Create();
			Type(input, "no trigger here");

			Assert.False(Key(input, System.Windows.Input.Key.Enter).Handled);
			Assert.False(Key(input, System.Windows.Input.Key.Down).Handled);
			Assert.False(Key(input, System.Windows.Input.Key.Escape).Handled);
		});
	}

	[Fact]
	public void M5_Enter_and_Tab_with_stale_items_are_not_swallowed()
	{
		StaTestRunner.Run(() =>
		{
			var (_, input, _, model, client) = Create();
			Type(input, "see #Fo");
			Assert.True(model.Autocomplete.IsOpen);
			client.AutocompleteHandler = (_, _) => new TaskCompletionSource<AutocompleteSuggestResult>().Task;   // the next answer never comes in time

			input.CaretIndex = 7;   // typed on at the caret: the items shown belong to "see #Fo"
			input.SelectedText = "x";

			Assert.False(Key(input, System.Windows.Input.Key.Enter).Handled);
			Assert.False(Key(input, System.Windows.Input.Key.Tab).Handled);
			Assert.Equal("see #Fox", input.Text);
		});
	}

	[Fact]
	public void Moving_the_caret_out_of_the_trigger_closes_the_popup()
	{
		StaTestRunner.Run(() =>
		{
			var (_, input, _, model, client) = Create();
			Type(input, "see #Fo and more");
			input.CaretIndex = 7;
			Assert.True(model.Autocomplete.IsOpen);

			// The client lets a '#' earlier on the line through (FR.7.14); the daemon's parser decides and answers none for a caret outside the trigger.
			client.AutocompleteAnswer = new(RemoteProtocol.Composer.AutocompleteModes.None, 0, 0, true, []);
			input.CaretIndex = 12;   // inside the next word

			var deadline = DateTime.UtcNow.AddSeconds(5);
			while (model.Autocomplete.IsOpen && DateTime.UtcNow < deadline)
			{
				Pump();
				System.Threading.Thread.Sleep(10);
			}

			Assert.False(model.Autocomplete.IsOpen);
		});
	}

	[Fact]
	public void A_dropped_path_is_the_panes_own_change_and_never_opens_the_popup()
	{
		StaTestRunner.Run(() =>
		{
			var (_, input, _, model, client) = Create();

			model.HandleFilesAsync([@"C:\Users\someone\file.txt"], 0, 0).GetAwaiter().GetResult();

			Assert.Equal(@"C:\Users\someone\file.txt", input.Text);
			Assert.False(model.Autocomplete.IsOpen);
			Assert.DoesNotContain(client.Calls, c => c.StartsWith("autocomplete.suggest:", StringComparison.Ordinal));
		});
	}

	[Fact]
	public void The_sign_in_notice_follows_the_auth_error_flag()
	{
		StaTestRunner.Run(() =>
		{
			var (_, _, _, model, client) = Create();
			var notices = new ComposerNotices { DataContext = model };
			var border = (Border)notices.FindName("AuthNotice");
			Pump();
			notices.Measure(new Size(800, 200));
			notices.Arrange(new Rect(0, 0, 800, 200));

			Assert.Equal(Visibility.Collapsed, border.Visibility);

			client.RaiseNotification(RemoteProtocol.Events.SessionChanged, new SessionChangedPayload(new SessionStateDto("a", false, false, null, false, null, null, false, null, true)));
			Assert.Equal(Visibility.Visible, border.Visibility);
			Assert.True(model.ReauthenticateCommand.CanExecute(null));

			client.RaiseNotification(RemoteProtocol.Events.SessionChanged, new SessionChangedPayload(new SessionStateDto("a", false, false, null, false, null, null, false, null, false)));
			Assert.Equal(Visibility.Collapsed, border.Visibility);
		});
	}
}
