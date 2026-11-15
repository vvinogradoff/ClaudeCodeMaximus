using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.WPF.Infrastructure;
using TessynDesktop.WPF.Rendering;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.ViewModels;
using TessynDesktop.WPF.Views;

namespace TessynDesktop.WPF.Tests;

/// <summary>
/// The real session pane on an STA thread: the drag-over, drop and paste handlers of the prompt input and the file-mode toggle, driven with
/// real routed events and real <see cref="DataObject"/>s (the OLE drag loop itself cannot be run without a mouse).
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class ComposerViewTests
{
	private static (SessionPane Pane, TextBox Input, ToggleButton Toggle, SessionPaneViewModel Model) Create()
	{
		var client = new FakeTessynClient();
		client.Sessions["a"] = FakeSession.WithEntries(3);
		var model = new SessionPaneViewModel(client, new FakeTranscriptSurface(), new MarkdigEntryRenderer(new UrlPolicy()), new FakeUrlLauncher(),
			new ImmediateUiDispatcher(), new SwitchLatencyTracker(NullLogger<SwitchLatencyTracker>.Instance), NullLogger<SessionPaneViewModel>.Instance);
		model.OpenAsync("a", "A").GetAwaiter().GetResult();
		var pane = new SessionPane { DataContext = model };
		pane.Measure(new Size(900, 700));
		pane.Arrange(new Rect(0, 0, 900, 700));
		pane.UpdateLayout();
		return (pane, (TextBox)pane.FindName("InputBox"), (ToggleButton)pane.FindName("FileModeToggle"), model);
	}

	private static DragEventArgs NewDragEventArgs(IDataObject data, RoutedEvent routedEvent, object source)
	{
		var ctor = typeof(DragEventArgs).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).Single();
		var args = (DragEventArgs)ctor.Invoke([data, DragDropKeyStates.None, DragDropEffects.Copy | DragDropEffects.Move, source, new Point(1, 1)]);
		args.RoutedEvent = routedEvent;
		return args;
	}

	private static IDataObject Files(params string[] paths)
	{
		var data = new DataObject();
		data.SetFileDropList([.. paths]);
		return data;
	}

	[Fact]
	public void Dropping_files_on_the_input_inserts_their_paths_at_the_caret_and_places_the_caret_after_them()
	{
		StaTestRunner.Run(() =>
		{
			var (_, input, _, model) = Create();
			input.Text = "AB";
			input.Select(1, 0);

			var over = NewDragEventArgs(Files(@"C:\x.txt"), DragDrop.PreviewDragOverEvent, input);
			input.RaiseEvent(over);
			var drop = NewDragEventArgs(Files(@"C:\x.txt", @"C:\y z.txt"), DragDrop.PreviewDropEvent, input);
			input.RaiseEvent(drop);

			Assert.True(over.Handled);
			Assert.Equal(DragDropEffects.Copy, over.Effects);
			Assert.True(drop.Handled);
			var inserted = @"C:\x.txt" + Environment.NewLine + @"C:\y z.txt";
			Assert.Equal("A" + inserted + "B", input.Text);
			Assert.Equal("A" + inserted + "B", model.InputText);
			Assert.Equal(1 + inserted.Length, input.CaretIndex);
		});
	}

	[Fact]
	public void Dropping_over_a_selection_replaces_it()
	{
		StaTestRunner.Run(() =>
		{
			var (_, input, _, _) = Create();
			input.Text = "see HERE ok";
			input.Select(4, 4);

			input.RaiseEvent(NewDragEventArgs(Files(@"C:\f.txt"), DragDrop.PreviewDropEvent, input));

			Assert.Equal(@"see C:\f.txt ok", input.Text);
			Assert.Equal(4 + @"C:\f.txt".Length, input.CaretIndex);
		});
	}

	[Fact]
	public void Other_drag_formats_keep_the_default_behaviour()
	{
		StaTestRunner.Run(() =>
		{
			var (_, input, _, _) = Create();
			input.Text = "keep";
			var text = new DataObject();
			text.SetText("dragged text");

			var over = NewDragEventArgs(text, DragDrop.PreviewDragOverEvent, input);
			input.RaiseEvent(over);
			var drop = NewDragEventArgs(text, DragDrop.PreviewDropEvent, input);
			input.RaiseEvent(drop);

			Assert.False(over.Handled);
			Assert.False(drop.Handled);
			Assert.Equal("keep", input.Text);
		});
	}

	[Fact]
	public void Pasting_a_file_list_behaves_like_a_drop_and_pasting_text_is_unchanged()
	{
		StaTestRunner.Run(() =>
		{
			var (_, input, _, _) = Create();
			input.Text = "go: ";
			input.Select(4, 0);

			var filesPaste = new DataObjectPastingEventArgs(Files(@"D:\a.txt", @"D:\b.txt"), isDragDrop: false, DataFormats.FileDrop) { RoutedEvent = DataObject.PastingEvent };
			input.RaiseEvent(filesPaste);
			Assert.True(filesPaste.CommandCancelled);
			var inserted = @"D:\a.txt" + Environment.NewLine + @"D:\b.txt";
			Assert.Equal("go: " + inserted, input.Text);
			Assert.Equal(4 + inserted.Length, input.CaretIndex);

			var text = new DataObject();
			text.SetText("plain text");
			var textPaste = new DataObjectPastingEventArgs(text, isDragDrop: false, DataFormats.UnicodeText) { RoutedEvent = DataObject.PastingEvent };
			input.RaiseEvent(textPaste);
			Assert.False(textPaste.CommandCancelled);

			// A clipboard that carries both files and text (some apps put the name as text) pastes the text.
			var both = new DataObject();
			both.SetFileDropList([@"D:\c.txt"]);
			both.SetText("c.txt");
			var bothPaste = new DataObjectPastingEventArgs(both, isDragDrop: false, DataFormats.UnicodeText) { RoutedEvent = DataObject.PastingEvent };
			input.RaiseEvent(bothPaste);
			Assert.False(bothPaste.CommandCancelled);
		});
	}

	[Fact]
	public void The_file_mode_toggle_is_a_named_button_bound_to_the_view_model()
	{
		StaTestRunner.Run(() =>
		{
			var (_, _, toggle, model) = Create();

			Assert.False(toggle.IsChecked);
			Assert.Equal("File mode", System.Windows.Automation.AutomationProperties.GetName(toggle));
			Assert.False(string.IsNullOrWhiteSpace(toggle.ToolTip as string));
			toggle.IsChecked = true;
			Assert.True(model.AttachAsFiles);
			model.AttachAsFiles = false;
			Assert.False(toggle.IsChecked);
		});
	}
}
