using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TessynDesktop.WPF.ViewModels;

namespace TessynDesktop.WPF.Views;

/// <summary>The Focus view: open items of the selected session as cards (FR.23). Pure view; everything is bound to <c>FocusViewModel</c>.</summary>
/// <remarks>Created by Claude</remarks>
public partial class FocusPanel : UserControl
{
	public FocusPanel()
	{
		InitializeComponent();
		PreviewKeyDown += OnPreviewKeyDown;
		IsVisibleChanged += OnIsVisibleChanged;
	}

	private Point _headerDown;
	private bool _headerClickArmed;

	/// <summary>The reply box's height range while its grip is dragged (FR.23.28).</summary>
	public const double MinReplyHeight = 56;

	public const double MaxReplyHeight = 600;

	/// <summary>The grip at the reply box's bottom right sets the box's height (FR.23.28); the width follows the card. Kept while the card is shown, not stored.</summary>
	private void OnReplyGripDragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
	{
		if (sender is FrameworkElement { Parent: Grid { Children: var children } } && children.OfType<TextBox>().FirstOrDefault() is { } box)
			ResizeReply(box, e.VerticalChange);
	}

	/// <summary>Grows or shrinks <paramref name="box"/> by <paramref name="delta"/>, from its current height, within <see cref="MinReplyHeight"/>..<see cref="MaxReplyHeight"/>.</summary>
	public static void ResizeReply(TextBox box, double delta)
	{
		var current = double.IsNaN(box.Height) ? box.ActualHeight : box.Height;
		box.Height = Math.Clamp(current + delta, MinReplyHeight, MaxReplyHeight);
	}

	/// <summary>
	/// A closed card's text is selectable (FR.23.25), which takes the mouse for selecting, so showing or hiding the body is a click detected here: a single
	/// press and release over the card's header (the chip row, or the id-and-title paragraph) without dragging (a drag is a selection and must not toggle).
	/// </summary>
	private void OnClosedCardDown(object sender, MouseButtonEventArgs e) =>
		PressOnClosedCard(e.GetPosition((IInputElement)sender), e.ClickCount, IsOverHeader((Border)sender, e));

	private void OnClosedCardUp(object sender, MouseButtonEventArgs e)
	{
		if (sender is FrameworkElement { DataContext: FocusItemViewModel item })
			ReleaseOnClosedCard(item, e.GetPosition((IInputElement)sender));
	}

	/// <summary>A press on a closed card (position in the card's coordinates): arms the header click only for a single press over the header.</summary>
	public void PressOnClosedCard(Point position, int clickCount, bool overHeader)
	{
		_headerDown = position;
		_headerClickArmed = clickCount == 1 && overHeader;
	}

	/// <summary>The release: toggles the body when the press was armed and the mouse did not travel (a drag is a selection and triggers nothing). True when it toggled.</summary>
	public bool ReleaseOnClosedCard(FocusItemViewModel item, Point position)
	{
		var armed = _headerClickArmed;
		_headerClickArmed = false;
		if (!armed)
			return false;

		var moved = position - _headerDown;
		if (Math.Abs(moved.X) > SystemParameters.MinimumHorizontalDragDistance || Math.Abs(moved.Y) > SystemParameters.MinimumVerticalDragDistance)
			return false;

		if (!item.ToggleBodyCommand.CanExecute(null))
			return false;

		item.ToggleBodyCommand.Execute(null);
		return true;
	}

	private static bool IsOverHeader(Border card, MouseButtonEventArgs e)
	{
		if (card.Child is not StackPanel panel)
			return false;

		if (panel.Children.OfType<DockPanel>().FirstOrDefault() is { } row && new Rect(row.RenderSize).Contains(e.GetPosition(row)))
			return true;

		if (panel.Children.OfType<FocusCardText>().FirstOrDefault() is { } text)
		{
			var point = e.GetPosition(text);
			return new Rect(text.RenderSize).Contains(point) && text.IsHeaderAt(point);
		}

		return false;
	}

	/// <summary>The panel shows when Focus is selected (its host is collapsed under Session): the Closed expander collapses again (FR.23.14).</summary>
	private void OnIsVisibleChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
	{
		if (e.NewValue is true && DataContext is FocusViewModel focus)
			focus.OnFocusSelected();
	}

	/// <summary>Ctrl+Enter in a reply box = "Send answers" (FR.23.15), through the same command as the button and under its enabling rule. Plain Enter still inserts a newline.</summary>
	private void OnPreviewKeyDown(object sender, KeyEventArgs e)
	{
		if (HandleReplyKey(e.OriginalSource, e.Key, Keyboard.Modifiers))
			e.Handled = true;
	}

	/// <summary>True when the key was Ctrl+Enter in a text box (handled, whether or not anything was sent); the send itself obeys the command's CanExecute.</summary>
	public bool HandleReplyKey(object? source, Key key, ModifierKeys modifiers)
	{
		if (source is not TextBox || !FocusSendShortcut.Matches(key, modifiers))
			return false;
		if (DataContext is FocusViewModel { SendAnswersCommand: var command } && command.CanExecute(null))
			command.Execute(null);
		return true;
	}
}
