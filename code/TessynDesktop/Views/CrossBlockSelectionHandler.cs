using System;
using System.Collections.Generic;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace TessynDesktop.Views;

/// <summary>
/// Enables text selection across multiple SelectableTextBlock controls within a
/// ScrollViewer. When a pointer drag crosses from one block into another, this
/// handler takes over selection management: partial selection on the first and
/// last blocks, full selection on all intermediate blocks, and Ctrl+C to copy
/// the combined text. Works in both text mode (cross-message) and markdown mode
/// (cross-block within and across messages).
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class CrossBlockSelectionHandler
{
	private readonly ScrollViewer _scroller;
	private SelectableTextBlock? _originBlock;
	private int _originAnchor;
	private bool _isCrossBlock;
	private readonly List<SelectableTextBlock> _selectedBlocks = new();
	private List<SelectableTextBlock>? _cachedBlocks;
	private string _crossBlockText = string.Empty;
	private DispatcherTimer? _autoScrollTimer;
	private double _autoScrollSpeed;

	private const double AutoScrollEdge = 30;
	private const double AutoScrollPixelsPerTick = 6;

	public CrossBlockSelectionHandler(ScrollViewer scroller)
	{
		_scroller = scroller;
		scroller.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
		scroller.AddHandler(InputElement.PointerMovedEvent, OnPointerMoved, RoutingStrategies.Tunnel);
		scroller.AddHandler(InputElement.PointerReleasedEvent, OnPointerReleased, RoutingStrategies.Tunnel);
		scroller.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
	}

	private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
	{
		if (!e.GetCurrentPoint(_scroller).Properties.IsLeftButtonPressed) return;

		ClearCrossSelection();
		_cachedBlocks = null;
		_originBlock = FindAncestorBlock(e.Source as Visual);
		_originAnchor = -1;
	}

	private void OnPointerMoved(object? sender, PointerEventArgs e)
	{
		if (_originBlock == null) return;
		if (!e.GetCurrentPoint(_scroller).Properties.IsLeftButtonPressed) return;

		var pos = e.GetPosition(_scroller);

		if (_isCrossBlock)
		{
			// Already in cross-block mode — update selection and block the event
			var endBlock = FindBlockAtPosition(pos);
			if (endBlock != null)
				UpdateCrossSelection(endBlock, pos);
			HandleAutoScroll(pos);
			e.Handled = true;
			return;
		}

		// Not yet in cross-block mode — check if the pointer crossed into a different block
		var currentBlock = FindBlockAtPosition(pos);
		if (currentBlock != null && currentBlock != _originBlock)
		{
			_isCrossBlock = true;
			_originAnchor = _originBlock.SelectionStart;
			_cachedBlocks = null; // rebuild on first use
			e.Pointer.Capture(null); // release the origin block's capture
			UpdateCrossSelection(currentBlock, pos);
			HandleAutoScroll(pos);
			e.Handled = true;
		}
	}

	private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
	{
		StopAutoScroll();
		_cachedBlocks = null;

		if (_isCrossBlock)
		{
			BuildCrossBlockText();
			e.Handled = true;
			// Keep state for Ctrl+C — cleared on next PointerPressed
		}
	}

	private void OnKeyDown(object? sender, KeyEventArgs e)
	{
		if (e.Key == Key.C && e.KeyModifiers.HasFlag(KeyModifiers.Control)
		    && _isCrossBlock && !string.IsNullOrEmpty(_crossBlockText))
		{
			TopLevel.GetTopLevel(_scroller)?.Clipboard?.SetTextAsync(_crossBlockText);
			e.Handled = true;
		}
	}

	// ── Selection logic ─────────────────────────────────────────────────────

	private void ClearCrossSelection()
	{
		foreach (var block in _selectedBlocks)
		{
			block.SelectionStart = 0;
			block.SelectionEnd = 0;
		}
		_selectedBlocks.Clear();
		_crossBlockText = string.Empty;
		_isCrossBlock = false;
	}

	private void UpdateCrossSelection(SelectableTextBlock endBlock, Point pointerPos)
	{
		var allBlocks = GetBlocks();
		var startIdx = allBlocks.IndexOf(_originBlock!);
		var endIdx = allBlocks.IndexOf(endBlock);
		if (startIdx < 0 || endIdx < 0) return;

		// Clear previous visual selection on tracked blocks
		foreach (var b in _selectedBlocks)
		{
			b.SelectionStart = 0;
			b.SelectionEnd = 0;
		}
		_selectedBlocks.Clear();

		bool forward = endIdx >= startIdx;
		int first = Math.Min(startIdx, endIdx);
		int last = Math.Max(startIdx, endIdx);

		for (int i = first; i <= last; i++)
		{
			var block = allBlocks[i];
			int len = GetTextLength(block);

			if (block == _originBlock)
			{
				int anchor = Math.Clamp(_originAnchor, 0, len);
				if (forward)
				{
					block.SelectionStart = anchor;
					block.SelectionEnd = len;
				}
				else
				{
					block.SelectionStart = 0;
					block.SelectionEnd = anchor;
				}
			}
			else if (block == endBlock)
			{
				int charIdx = EstimateCharIndex(block, pointerPos);
				if (forward)
				{
					block.SelectionStart = 0;
					block.SelectionEnd = charIdx;
				}
				else
				{
					block.SelectionStart = charIdx;
					block.SelectionEnd = len;
				}
			}
			else
			{
				// Intermediate block — select all
				block.SelectionStart = 0;
				block.SelectionEnd = len;
			}

			_selectedBlocks.Add(block);
		}
	}

	private void BuildCrossBlockText()
	{
		var sb = new StringBuilder();
		foreach (var block in _selectedBlocks)
		{
			var text = GetText(block);
			int s = Math.Min(block.SelectionStart, block.SelectionEnd);
			int e = Math.Max(block.SelectionStart, block.SelectionEnd);
			s = Math.Clamp(s, 0, text.Length);
			e = Math.Clamp(e, 0, text.Length);

			if (e > s)
			{
				if (sb.Length > 0) sb.AppendLine();
				sb.Append(text, s, e - s);
			}
		}
		_crossBlockText = sb.ToString();
	}

	// ── Helpers ──────────────────────────────────────────────────────────────

	private static SelectableTextBlock? FindAncestorBlock(Visual? visual)
	{
		while (visual != null)
		{
			if (visual is SelectableTextBlock stb)
				return stb;
			visual = visual.GetVisualParent();
		}
		return null;
	}

	private List<SelectableTextBlock> GetBlocks()
	{
		if (_cachedBlocks != null) return _cachedBlocks;
		_cachedBlocks = new List<SelectableTextBlock>();
		Collect(_scroller, _cachedBlocks);
		return _cachedBlocks;
	}

	private static void Collect(Visual parent, List<SelectableTextBlock> result)
	{
		foreach (var child in parent.GetVisualChildren())
		{
			if (!child.IsVisible) continue;
			if (child is SelectableTextBlock stb)
				result.Add(stb);
			else
				Collect(child, result);
		}
	}

	private SelectableTextBlock? FindBlockAtPosition(Point scrollerPos)
	{
		var blocks = GetBlocks();
		foreach (var block in blocks)
		{
			var transform = block.TransformToVisual(_scroller);
			if (transform == null) continue;

			double top = transform.Value.Transform(new Point(0, 0)).Y;
			double bottom = transform.Value.Transform(new Point(0, block.Bounds.Height)).Y;

			if (scrollerPos.Y >= top && scrollerPos.Y <= bottom)
				return block;
		}
		return null;
	}

	private int EstimateCharIndex(SelectableTextBlock block, Point scrollerPos)
	{
		var transform = block.TransformToVisual(_scroller);
		if (transform == null) return 0;

		double blockTop = transform.Value.Transform(new Point(0, 0)).Y;
		double blockH = block.Bounds.Height;
		int len = GetTextLength(block);
		if (blockH <= 0 || len == 0) return 0;

		double ratio = Math.Clamp((scrollerPos.Y - blockTop) / blockH, 0, 1);
		return (int)(ratio * len);
	}

	private static int GetTextLength(SelectableTextBlock block) => GetText(block).Length;

	private static string GetText(SelectableTextBlock block)
	{
		var text = block.Text;
		if (string.IsNullOrEmpty(text))
			text = block.Inlines?.Text;
		return text ?? string.Empty;
	}

	// ── Auto-scroll ─────────────────────────────────────────────────────────

	private void HandleAutoScroll(Point pointerPos)
	{
		double viewH = _scroller.Viewport.Height;
		if (pointerPos.Y < AutoScrollEdge)
		{
			_autoScrollSpeed = -AutoScrollPixelsPerTick;
			StartAutoScroll();
		}
		else if (pointerPos.Y > viewH - AutoScrollEdge)
		{
			_autoScrollSpeed = AutoScrollPixelsPerTick;
			StartAutoScroll();
		}
		else
		{
			StopAutoScroll();
		}
	}

	private void StartAutoScroll()
	{
		if (_autoScrollTimer != null) return;
		_autoScrollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
		_autoScrollTimer.Tick += (_, _) =>
		{
			double max = _scroller.Extent.Height - _scroller.Viewport.Height;
			double next = Math.Clamp(_scroller.Offset.Y + _autoScrollSpeed, 0, Math.Max(0, max));
			_scroller.Offset = new Vector(0, next);
		};
		_autoScrollTimer.Start();
	}

	private void StopAutoScroll()
	{
		_autoScrollTimer?.Stop();
		_autoScrollTimer = null;
	}
}
