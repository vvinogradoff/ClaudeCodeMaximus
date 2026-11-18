using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TessynDesktop.WPF.ViewModels.Settings;

namespace TessynDesktop.WPF.Views.Settings;

/// <summary>
/// The colour picker (DataContext: <see cref="ColourPickerViewModel"/>). The saturation/value square is the only part that needs code: the mouse drags a point in it, the thumb
/// follows the view model's saturation and value.
/// </summary>
/// <remarks>Created by Claude</remarks>
public partial class ColourPickerControl : UserControl
{
	private ColourPickerViewModel? _model;
	private bool _dragging;

	public ColourPickerControl()
	{
		InitializeComponent();
		// The picker may grow (named colours, harmonies) but fits on the screen: past the work area it scrolls.
		Scroller.MaxHeight = Math.Max(240, SystemParameters.WorkArea.Height - 48);
		DataContextChanged += (_, _) => Attach();
		Unloaded += (_, _) => Detach();
		Loaded += (_, _) =>
		{
			Attach();
			Focus();   // Esc and Enter reach the picker
		};
	}

	/// <summary>Esc drops the edit and closes the picker; Enter keeps the new colour (pending on its row) and closes it.</summary>
	private void OnKeyDown(object sender, KeyEventArgs e)
	{
		if (_model == null)
			return;

		if (e.Key == Key.Escape)
		{
			_model.Cancel();
			e.Handled = true;
		}
		else if (e.Key == Key.Enter)
		{
			_model.Accept();
			e.Handled = true;
		}
	}

	private void Attach()
	{
		Detach();
		_model = DataContext as ColourPickerViewModel;
		if (_model != null)
			_model.PropertyChanged += OnModelChanged;

		PlaceThumb();
	}

	private void Detach()
	{
		if (_model != null)
			_model.PropertyChanged -= OnModelChanged;

		_model = null;
	}

	private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName is nameof(ColourPickerViewModel.Saturation) or nameof(ColourPickerViewModel.Value))
			PlaceThumb();
	}

	private void OnSquareSizeChanged(object sender, SizeChangedEventArgs e) => PlaceThumb();

	/// <summary>The thumb is where the saturation (x) and value (y, from the bottom) are.</summary>
	private void PlaceThumb()
	{
		if (_model == null || Square.ActualWidth <= 0)
			return;

		Canvas.SetLeft(Thumb, _model.Saturation * Square.ActualWidth - Thumb.Width / 2);
		Canvas.SetTop(Thumb, (1 - _model.Value) * Square.ActualHeight - Thumb.Height / 2);
	}

	private void Pick(Point at)
	{
		if (_model == null || Square.ActualWidth <= 0 || Square.ActualHeight <= 0)
			return;

		_model.PickSquare(at.X / Square.ActualWidth, 1 - at.Y / Square.ActualHeight);
	}

	private void OnSquareDown(object sender, MouseButtonEventArgs e)
	{
		_dragging = Square.CaptureMouse();
		Pick(e.GetPosition(Square));
		e.Handled = true;
	}

	private void OnSquareMove(object sender, MouseEventArgs e)
	{
		if (_dragging && e.LeftButton == MouseButtonState.Pressed)
			Pick(e.GetPosition(Square));
	}

	private void OnSquareUp(object sender, MouseButtonEventArgs e)
	{
		if (!_dragging)
			return;

		_dragging = false;
		Square.ReleaseMouseCapture();
		Pick(e.GetPosition(Square));
	}
}
