using System.Windows;
using System.Windows.Controls;

namespace TessynDesktop.WPF.Views;

/// <summary>The face of a session in the tree and in the session lists (DataContext: a tree node or a list row).</summary>
/// <remarks>Created by Claude</remarks>
public partial class SessionRowFace : UserControl
{
	/// <summary>Room kept free on the right of the first line for a button laid over the row (the Hide or favourite button of a list row), so the tint can span the full width.</summary>
	public static readonly DependencyProperty ReservedRightProperty = DependencyProperty.Register(nameof(ReservedRight), typeof(double), typeof(SessionRowFace),
		new PropertyMetadata(0.0, (d, e) => ((SessionRowFace)d).FirstLine.Margin = new Thickness(0, 0, (double)e.NewValue, 0)));

	public SessionRowFace()
	{
		InitializeComponent();
	}

	public double ReservedRight
	{
		get => (double)GetValue(ReservedRightProperty);
		set => SetValue(ReservedRightProperty, value);
	}
}