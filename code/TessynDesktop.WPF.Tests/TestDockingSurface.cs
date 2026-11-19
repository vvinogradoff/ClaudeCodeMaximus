using AvalonDock;
using AvalonDock.Layout;
using TessynDesktop.WPF.Views;

namespace TessynDesktop.WPF.Tests;

/// <summary>An <see cref="IDockingSurface"/> over a bare layout model (the session document in a horizontal panel), optionally attached to a <see cref="DockingManager"/>.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class TestDockingSurface : IDockingSurface
{
	public TestDockingSurface(double width = 1000, double height = 600, DockingManager? manager = null)
	{
		AreaWidth = width;
		AreaHeight = height;
		var document = new LayoutDocument { ContentId = "session", Title = "Session", Content = "session", CanClose = false, CanFloat = false };
		DocumentPane = new LayoutDocumentPane(document);
		var panel = new LayoutPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
		panel.Children.Add(DocumentPane);
		Root = new LayoutRoot { RootPanel = panel };
		if (manager != null)
			manager.Layout = Root;

		Root.Updated += (_, _) => LayoutChanged?.Invoke(this, EventArgs.Empty);
	}

	public LayoutRoot Root { get; }

	public LayoutDocumentPane DocumentPane { get; }

	public double AreaWidth { get; set; }

	public double AreaHeight { get; set; }

	public event EventHandler? LayoutChanged;

	public void RaiseLayoutChanged() => LayoutChanged?.Invoke(this, EventArgs.Empty);
}
