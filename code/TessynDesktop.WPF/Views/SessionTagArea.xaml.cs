using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using TessynDesktop.WPF.ViewModels;

namespace TessynDesktop.WPF.Views;

/// <summary>
/// The session tag area (FR.24): the chips under the session header in two modes. "Mini" shows exactly one line of chips (the rest is
/// clipped, a "+N" hint counts them); "full" grows to fit the chips up to ten lines and scrolls beyond that. A click on empty space (or the
/// hint) expands, a double-click on empty space (not on a chip) collapses. The line height is measured from the laid-out chips, not hard-coded.
/// DataContext: <see cref="TagAreaViewModel"/>.
/// </summary>
/// <remarks>Created by Claude</remarks>
public partial class SessionTagArea : UserControl
{
	public static readonly DependencyProperty HiddenCountProperty = DependencyProperty.Register(
		nameof(HiddenCount), typeof(int), typeof(SessionTagArea), new PropertyMetadata(0));

	private TagAreaViewModel? _model;
	private Panel? _chipsPanel;
	private bool _expandedByThisClick;

	public SessionTagArea()
	{
		InitializeComponent();
		DataContextChanged += (_, _) => Attach(DataContext as TagAreaViewModel);
	}

	/// <summary>In mini mode: how many chips the single line hides; 0 in full mode.</summary>
	public int HiddenCount
	{
		get => (int)GetValue(HiddenCountProperty);
		private set => SetValue(HiddenCountProperty, value);
	}

	/// <summary>Whether the session has any tag chip at all.</summary>
	public bool HasTags => _model is { Tags.Count: > 0 };

	/// <summary>Height of one line of chips (the real chip height plus its margin); 0 until laid out or without chips.</summary>
	public double LineHeight { get; private set; }

	/// <summary>The height the area currently takes; 0 without chips.</summary>
	public double AreaHeight => HasTags ? Scroller.ActualHeight : 0;

	public bool IsExpanded => _model?.IsExpanded ?? false;

	public void Expand()
	{
		// Without tags there is nothing to expand.
		if (_model != null && HasTags)
			_model.IsExpanded = true;
	}

	public void Collapse()
	{
		if (_model != null)
			_model.IsExpanded = false;
	}

	private void Attach(TagAreaViewModel? model)
	{
		if (_model != null)
		{
			_model.PropertyChanged -= OnModelPropertyChanged;
			_model.Tags.CollectionChanged -= OnTagsChanged;
		}

		_model = model;
		if (_model != null)
		{
			_model.PropertyChanged += OnModelPropertyChanged;
			_model.Tags.CollectionChanged += OnTagsChanged;
		}

		InvalidateMeasure();
		RaiseAutomationState();
	}

	private void OnTagsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
	{
		InvalidateMeasure();
		RaiseAutomationState();
	}

	private void OnModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName != nameof(TagAreaViewModel.IsExpanded))
			return;

		if (!_model!.IsExpanded)
			Scroller.ScrollToVerticalOffset(0);
		InvalidateMeasure();
		RaiseAutomationState();
	}

	protected override Size ArrangeOverride(Size arrangeBounds)
	{
		var size = base.ArrangeOverride(arrangeBounds);
		UpdateMetrics();
		return size;
	}

	/// <summary>Reads the chips' laid-out positions and sets the area's height, the scrollbar and the "+N" hint for the current mode.</summary>
	private void UpdateMetrics()
	{
		_chipsPanel ??= FindPanel(Chips);
		var chips = _chipsPanel?.Children.OfType<FrameworkElement>().ToList() ?? [];
		IsTabStop = chips.Count > 0;

		if (chips.Count == 0)
		{
			LineHeight = 0;
			Apply(double.NaN, hidden: 0, scroll: false);
			return;
		}

		var tops = chips.Select(c => c.TranslatePoint(default, _chipsPanel!).Y).ToList();
		var lineHeight = 0.0;
		var contentBottom = 0.0;
		for (var i = 0; i < chips.Count; i++)
		{
			if (tops[i] < Constants.TagArea.LineEpsilon)
				lineHeight = Math.Max(lineHeight, chips[i].RenderSize.Height);
			contentBottom = Math.Max(contentBottom, tops[i] + chips[i].RenderSize.Height);
		}

		LineHeight = lineHeight;
		if (lineHeight <= 0)
		{
			// Chips not measured yet (or squeezed to nothing by a zero-height clip): drop the clip so the next pass sees their real height.
			Apply(double.NaN, hidden: 0, scroll: false);
			return;
		}

		if (IsExpanded)
		{
			Apply(Math.Min(contentBottom, lineHeight * Constants.TagArea.MaxFullLines), hidden: 0, scroll: true);
			return;
		}

		var hidden = tops.Count(top => top >= lineHeight - Constants.TagArea.LineEpsilon);
		Apply(lineHeight, hidden, scroll: false);
	}

	private void Apply(double height, int hidden, bool scroll)
	{
		if (!SameHeight(Scroller.Height, height))
			Scroller.Height = height;

		var visibility = scroll ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
		if (Scroller.VerticalScrollBarVisibility != visibility)
			Scroller.VerticalScrollBarVisibility = visibility;

		if (HiddenCount != hidden)
		{
			HiddenCount = hidden;
			HiddenHintText.Text = string.Format(Constants.TagArea.HiddenHintFormat, hidden);
			HiddenHint.ToolTip = string.Format(Constants.TagArea.HiddenHintToolTipFormat, hidden);
		}

		var hintVisibility = hidden > 0 ? Visibility.Visible : Visibility.Collapsed;
		if (HiddenHint.Visibility != hintVisibility)
			HiddenHint.Visibility = hintVisibility;

		// Reserve the hint's width at the right end of the line so it never covers a chip. Narrowing the line can only hide more chips, never
		// fewer, so reserving once the hint is needed cannot flip the layout back and forth.
		var reserved = 0.0;
		if (hidden > 0)
		{
			HiddenHint.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
			reserved = Math.Ceiling(HiddenHint.DesiredSize.Width);
		}

		if (Math.Abs(Scroller.Margin.Right - reserved) > 0.01)
			Scroller.Margin = new Thickness(0, 0, reserved, 0);
	}

	private static bool SameHeight(double current, double wanted) =>
		double.IsNaN(current) || double.IsNaN(wanted) ? double.IsNaN(current) && double.IsNaN(wanted) : Math.Abs(current - wanted) < 0.01;

	private static Panel? FindPanel(DependencyObject root)
	{
		for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
		{
			var child = VisualTreeHelper.GetChild(root, i);
			if (child is Panel { IsItemsHost: true } panel)
				return panel;

			var nested = FindPanel(child);
			if (nested != null)
				return nested;
		}

		return null;
	}

	// ---- mouse and keyboard ----

	protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
	{
		base.OnPreviewMouseLeftButtonDown(e);
		if (_model == null || !HasTags)
			return;

		// The WPF client does not reference Core, so this mirrors Core's TagAreaGestureInterpreter (the Avalonia client's, unit tested there).
		// The vertical scrollbar belongs to the scrolling, not to the mode gestures: a quick double-click on its arrow or track must scroll.
		var source = e.OriginalSource as DependencyObject;
		if (IsInScrollBar(source))
			return;

		if (e.ClickCount >= 2)
		{
			// Every further press of the click that just expanded the area (second, third, ...) is part of that gesture, not a request to
			// fold it again; the flag is cleared only by the next first press. A double-click collapses only on empty space (FR.24: "on the
			// white space"), never on a chip: a chip press keeps its own meaning.
			if (_expandedByThisClick)
				e.Handled = true;
			else if (IsExpanded && !IsOnChip(source))
			{
				_model.IsExpanded = false;
				e.Handled = true;
			}

			return;
		}

		_expandedByThisClick = false;
		if (!IsExpanded && !IsOnChip(source))
		{
			_model.IsExpanded = true;
			_expandedByThisClick = true;
		}
	}

	private bool IsInScrollBar(DependencyObject? source)
	{
		while (source != null && !ReferenceEquals(source, this))
		{
			if (source is ScrollBar)
				return true;

			source = source is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source);
		}

		return false;
	}

	private bool IsOnChip(DependencyObject? source)
	{
		while (source != null && !ReferenceEquals(source, this))
		{
			if (source is ButtonBase)
				return true;

			source = source is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source);
		}

		return false;
	}

	protected override void OnKeyDown(KeyEventArgs e)
	{
		base.OnKeyDown(e);
		if (_model == null || e.Handled || !ReferenceEquals(e.OriginalSource, this))
			return;

		if (e.Key is Key.Enter or Key.Space)
		{
			_model.IsExpanded = !_model.IsExpanded;
			e.Handled = true;
		}
	}

	// ---- UI Automation ----

	protected override AutomationPeer OnCreateAutomationPeer() => new SessionTagAreaAutomationPeer(this);

	private void RaiseAutomationState()
	{
		if (UIElementAutomationPeer.FromElement(this) is SessionTagAreaAutomationPeer peer)
			peer.RaiseStateChanged();
	}
}
