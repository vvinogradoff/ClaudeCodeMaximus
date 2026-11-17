using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using TessynDesktop.Models;
using TessynDesktop.Services;
using TessynDesktop.ViewModels;

namespace TessynDesktop.Views;

/// <summary>
/// The session tag area (FR.24.16): the chips under the session header in two modes. "Mini" shows exactly one line of chips (the rest is
/// clipped, a "+N" hint counts them); "full" grows to fit the chips up to ten lines and scrolls beyond that. A click on empty space (or the hint)
/// expands, a double-click on empty space (not on a chip) collapses; a chip click opens its link. The decisions live in <see cref="TagAreaLayoutCalculator"/>
/// and <see cref="TagAreaGestureInterpreter"/>; this control only measures the laid-out chips and applies the result.
/// DataContext: <see cref="SessionTagsViewModel"/>.
/// </summary>
/// <remarks>Created by Claude</remarks>
public partial class SessionTagArea : UserControl
{
	private readonly TagAreaLayoutCalculator _layout = new();
	private readonly TagAreaGestureInterpreter _gestures = new();
	private SessionTagsViewModel? _model;

	public SessionTagArea()
	{
		InitializeComponent();

		// Tunnel, so the area sees a press on a chip before the chip does (the interpreter then leaves a chip press alone).
		AddHandler(PointerPressedEvent, OnAreaPointerPressed, RoutingStrategies.Tunnel);
		AddHandler(KeyDownEvent, OnAreaKeyDown, RoutingStrategies.Bubble);
		AttachedToVisualTree += (_, _) => _model?.RefreshMode();
	}

	private bool IsExpanded => _model?.IsExpanded ?? false;

	protected override void OnDataContextChanged(EventArgs e)
	{
		base.OnDataContextChanged(e);

		if (_model != null)
		{
			_model.PropertyChanged -= OnModelPropertyChanged;
			_model.Chips.CollectionChanged -= OnChipsChanged;
		}

		_model = DataContext as SessionTagsViewModel;
		if (_model != null)
		{
			_model.PropertyChanged += OnModelPropertyChanged;
			_model.Chips.CollectionChanged += OnChipsChanged;
			_model.RefreshMode();
		}

		InvalidateMeasure();
	}

	private void OnChipsChanged(object? sender, NotifyCollectionChangedEventArgs e) => InvalidateMeasure();

	private void OnModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName != nameof(SessionTagsViewModel.IsExpanded))
			return;

		if (!IsExpanded)
			Scroller.Offset = default;
		InvalidateMeasure();
	}

	protected override Size ArrangeOverride(Size finalSize)
	{
		var size = base.ArrangeOverride(finalSize);
		UpdateMetrics();
		return size;
	}

	/// <summary>Reads the chips' laid-out positions and sets the area's height, the scrollbar and the "+N" hint for the current mode.</summary>
	private void UpdateMetrics()
	{
		var boxes = new List<TagChipBox>();
		if (Chips.ItemsPanelRoot is { } panel)
		{
			foreach (var child in panel.Children)
				boxes.Add(new TagChipBox(child.Bounds.Top, child.Bounds.Height, child.Bounds.Width));
		}

		// The line the chips would have without the hint's reserve: the current width plus what is reserved now.
		var unreservedWidth = Scroller.Bounds.Width + Scroller.Margin.Right;
		var metrics = _layout.Compute(boxes, IsExpanded, unreservedWidth);
		Apply(metrics);
	}

	private void Apply(TagAreaMetrics metrics)
	{
		var height = metrics.AreaHeight ?? double.NaN;
		if (!SameHeight(Scroller.Height, height))
			Scroller.Height = height;

		var visibility = metrics.Scroll ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
		if (Scroller.VerticalScrollBarVisibility != visibility)
			Scroller.VerticalScrollBarVisibility = visibility;

		var hintText = string.Format(Constants.TagArea.HiddenHintFormat, metrics.HiddenCount);
		if (!string.Equals(HiddenHintText.Text, hintText, StringComparison.Ordinal))
		{
			HiddenHintText.Text = hintText;
			ToolTip.SetTip(HiddenHint, string.Format(Constants.TagArea.HiddenHintToolTipFormat, metrics.HiddenCount));
		}

		var showHint = metrics.HiddenCount > 0;
		if (HiddenHint.IsVisible != showHint)
			HiddenHint.IsVisible = showHint;

		// Reserve the hint's width at the right end of the line so it never covers a chip. Narrowing the line can only hide more chips, never
		// fewer, so reserving once the hint is needed cannot flip the layout back and forth.
		var reserved = 0.0;
		if (showHint)
		{
			HiddenHint.Measure(Size.Infinity);
			reserved = Math.Ceiling(HiddenHint.DesiredSize.Width);
		}

		if (Math.Abs(Scroller.Margin.Right - reserved) > 0.01)
			Scroller.Margin = new Thickness(0, 0, reserved, 0);
	}

	private static bool SameHeight(double current, double wanted) =>
		double.IsNaN(current) || double.IsNaN(wanted) ? double.IsNaN(current) && double.IsNaN(wanted) : Math.Abs(current - wanted) < 0.01;

	// ---- mouse and keyboard ----

	private void OnAreaPointerPressed(object? sender, PointerPressedEventArgs e)
	{
		if (_model == null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
			return;

		var source = e.Source as Visual;
		var action = _gestures.OnPress(e.ClickCount, IsOnChip(source), IsInScrollBar(source), IsExpanded, _model.HasTags);
		switch (action)
		{
			case TagAreaGestureAction.Expand:
				_model.IsExpanded = true;
				break;

			case TagAreaGestureAction.Collapse:
				_model.IsExpanded = false;
				e.Handled = true;
				break;

			case TagAreaGestureAction.Swallow:
				e.Handled = true;
				break;
		}
	}

	private static bool IsInScrollBar(Visual? source) => source?.FindAncestorOfType<ScrollBar>(includeSelf: true) != null;

	private static bool IsOnChip(Visual? source)
	{
		for (var v = source; v != null; v = v.GetVisualParent())
		{
			if (v is Border border && border.Classes.Contains("tagChip"))
				return true;
		}

		return false;
	}

	/// <summary>A tag chip opens its URL in the default browser (FR.24.7); the launcher enforces the URL policy.</summary>
	private void OnTagChipPressed(object? sender, PointerPressedEventArgs e)
	{
		if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
			return;

		if (sender is Control { DataContext: SessionTagChipViewModel chip })
		{
			chip.Open();
			e.Handled = true;
		}
	}

	private void OnAreaKeyDown(object? sender, KeyEventArgs e)
	{
		if (_model == null || e.Handled || !ReferenceEquals(e.Source, this) || !_model.HasTags)
			return;

		if (e.Key is Key.Enter or Key.Space)
		{
			_model.IsExpanded = !_model.IsExpanded;
			e.Handled = true;
		}
	}
}
