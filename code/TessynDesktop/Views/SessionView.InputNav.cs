using System;
using System.Collections.Generic;
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
/// Input selection and keyboard navigation of the transcript (FR.2.6). A click on an input's bubble selects it (thin dotted border); Esc or a
/// click elsewhere clears it. Up/Down walk the inputs (the clicked input decides the walk kind: automated = every input, mine = only mine) and
/// scroll the selected one into view; with nothing selected the arrows scroll by a line and PgUp/PgDn by a page. The decisions are made by
/// <see cref="InputNavigator"/>; this part only maps pointer and key events onto them.
/// </summary>
/// <remarks>Created by Claude</remarks>
public partial class SessionView
{
	private readonly InputNavigator _inputNav = new();
	private MessageEntryViewModel? _selectedInput;
	private bool _walkAllInputs;
	private MessageEntryViewModel? _pressedInput;
	private Point _pressPoint;
	private SelectableTextBlock? _pressedBlock;

	private void InitInputNavigation()
	{
		// Tunnel, like the cross-block text selection, so a press on selectable text is seen before the text block handles it.
		MessageScroller.AddHandler(InputElement.PointerPressedEvent, OnTranscriptPointerPressed, RoutingStrategies.Tunnel);
		MessageScroller.AddHandler(InputElement.PointerReleasedEvent, OnTranscriptPointerReleased, RoutingStrategies.Tunnel);
		MessageScroller.AddHandler(InputElement.KeyDownEvent, OnTranscriptKeyDown, RoutingStrategies.Tunnel);
	}

	private void OnTranscriptPointerPressed(object? sender, PointerPressedEventArgs e)
	{
		if (!e.GetCurrentPoint(MessageScroller).Properties.IsLeftButtonPressed)
			return;

		var source = e.Source as Visual;

		// The scrollbar keeps its own meaning: dragging it neither selects nor clears.
		if (source?.FindAncestorOfType<ScrollBar>(includeSelf: true) != null)
		{
			_pressedInput = null;
			return;
		}

		MessageScroller.Focus();
		_pressPoint = e.GetPosition(MessageScroller);
		_pressedBlock = source?.FindAncestorOfType<SelectableTextBlock>(includeSelf: true);
		_pressedInput = InputAt(source);

		// A click outside every input bubble is a click elsewhere in the transcript.
		if (_pressedInput == null)
			ClearInputSelection();
	}

	private void OnTranscriptPointerReleased(object? sender, PointerReleasedEventArgs e)
	{
		var pressed = _pressedInput;
		_pressedInput = null;
		if (pressed == null)
			return;

		var moved = e.GetPosition(MessageScroller) - _pressPoint;
		var distance = Math.Sqrt(moved.X * moved.X + moved.Y * moved.Y);
		var madeTextSelection = _pressedBlock != null && _pressedBlock.SelectionStart != _pressedBlock.SelectionEnd;
		if (_inputNav.IsSelectingClick(distance, madeTextSelection))
			SelectInput(pressed, walkAll: pressed.IsAutomated);
	}

	/// <summary>The input whose bubble contains the visual, or null.</summary>
	private static MessageEntryViewModel? InputAt(Visual? source)
	{
		for (var v = source; v != null; v = v.GetVisualParent())
		{
			if (v is Border border && border.Classes.Contains("inputBubble"))
				return border.DataContext as MessageEntryViewModel;
		}

		return null;
	}

	private void SelectInput(MessageEntryViewModel input, bool walkAll)
	{
		if (!ReferenceEquals(_selectedInput, input))
		{
			if (_selectedInput != null)
				_selectedInput.IsSelected = false;
			_selectedInput = input;
			input.IsSelected = true;
		}

		_walkAllInputs = walkAll;
	}

	private void ClearInputSelection()
	{
		if (_selectedInput != null)
			_selectedInput.IsSelected = false;
		_selectedInput = null;
	}

	/// <summary>Drops the selection when its entry left the list (a reload or a rewrite); appends keep it.</summary>
	private void DropSelectionIfGone(NotifyCollectionChangedEventArgs e)
	{
		if (_selectedInput == null || _subscribedVm == null || e.Action == NotifyCollectionChangedAction.Add)
			return;

		if (!_subscribedVm.Messages.Contains(_selectedInput))
			ClearInputSelection();
	}

	private void OnTranscriptKeyDown(object? sender, KeyEventArgs e)
	{
		var key = e.Key switch
		{
			Key.Up       => (InputNavKey?)InputNavKey.Up,
			Key.Down     => InputNavKey.Down,
			Key.PageUp   => InputNavKey.PageUp,
			Key.PageDown => InputNavKey.PageDown,
			Key.Escape   => InputNavKey.Escape,
			_            => null,
		};
		if (key is null)
			return;

		var action = _inputNav.KeyAction(key.Value, e.KeyModifiers != KeyModifiers.None, _selectedInput != null, MessageScroller.Viewport.Height);
		switch (action.Kind)
		{
			case InputKeyActionKind.Navigate:
				NavigateInputs(action.Direction);
				e.Handled = true;
				break;

			case InputKeyActionKind.Scroll:
				ScrollTranscriptBy(action.ScrollDelta);
				e.Handled = true;
				break;

			case InputKeyActionKind.Clear:
				ClearInputSelection();
				e.Handled = true;
				break;
		}
	}

	private void NavigateInputs(int direction)
	{
		if (_selectedInput == null || _subscribedVm == null)
			return;

		var messages = _subscribedVm.Messages;
		var from = messages.IndexOf(_selectedInput);
		if (from < 0)
			return;

		var items = new List<InputNavItem>(messages.Count);
		foreach (var message in messages)
			items.Add(new InputNavItem(message.IsUser, message.IsAutomated));

		var next = _inputNav.FindNext(items, from, direction, _walkAllInputs);
		if (next < 0)
			return;

		// The walk kind is the one of the input the user clicked: it is kept while the arrows move on.
		SelectInput(messages[next], _walkAllInputs);
		RevealInput(next);
	}

	private void RevealInput(int index)
	{
		if (MessageList.ContainerFromIndex(index) is not Control container)
			return;

		var top = container.TranslatePoint(default, MessageScroller);
		if (top is null)
			return;

		var offset = MessageScroller.Offset.Y;
		var target = _inputNav.OffsetToReveal(offset, MessageScroller.Viewport.Height, offset + top.Value.Y, container.Bounds.Height,
			Constants.InputNav.RevealMarginPx);
		SetTranscriptOffset(target);
	}

	private void ScrollTranscriptBy(double delta) => SetTranscriptOffset(MessageScroller.Offset.Y + delta);

	private void SetTranscriptOffset(double y)
	{
		var max = Math.Max(0, MessageScroller.Extent.Height - MessageScroller.Viewport.Height);
		MessageScroller.Offset = new Vector(MessageScroller.Offset.X, Math.Clamp(y, 0, max));
	}
}
