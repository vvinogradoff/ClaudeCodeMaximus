using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;
using AvalonDock;
using AvalonDock.Controls;
using AvalonDock.Layout;
using TessynDesktop.WPF.Services;

namespace TessynDesktop.WPF.Views;

/// <summary>
/// Keeps the keyboard focus where it is while the layout changes (FR.6.20). The library gives the focus to the last focused element of whatever content becomes active
/// (<c>FocusElementManager</c>); a focus move from one panel into another within a short time of a real docking change and without any input of the user is cancelled.
/// <list type="bullet">
/// <item>Input is noted at application level (<see cref="InputManager.PreProcessInput"/>: mouse, key, touch, stylus, from any window of the process: floating panels, popups,
/// menus) and from the message loop (<see cref="ComponentDispatcher.ThreadPreprocessMessage"/>: button and pointer down messages). A focus move into a native child window
/// (the WebView2: an <see cref="HwndHost"/>) is never cancelled, since native content only takes the focus by the user's click or Tab.</item>
/// <item>A docking change is a change of the layout model (<c>Root.Updated</c>, elements added or removed, the active content), never <c>UIElement.LayoutUpdated</c>, which fires
/// on every layout pass.</item>
/// </list>
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class DockingFocusGuard : IDisposable
{
	private const int WmLButtonDown = 0x0201;
	private const int WmRButtonDown = 0x0204;
	private const int WmMButtonDown = 0x0207;
	private const int WmXButtonDown = 0x020B;
	private const int WmKeyDown = 0x0100;
	private const int WmSysKeyDown = 0x0104;
	private const int WmPointerDown = 0x0246;
	private const int WmTouch = 0x0240;

	private readonly DockingManager _manager;
	private readonly DependencyPropertyDescriptor? _layoutProperty;
	private LayoutRoot? _hooked;
	private bool _subscribed;

	public DockingFocusGuard(DockingManager manager, DockingFocusPolicy? policy = null)
	{
		_manager = manager;
		Policy = policy ?? new DockingFocusPolicy();
		manager.ActiveContentChanged += OnDockingChanged;
		manager.PreviewLostKeyboardFocus += OnPreviewLostKeyboardFocus;
		_layoutProperty = DependencyPropertyDescriptor.FromProperty(DockingManager.LayoutProperty, typeof(DockingManager));
		_layoutProperty?.AddValueChanged(manager, OnLayoutReplaced);
		HookLayout();
		manager.Loaded += (_, _) => Subscribe();
		manager.Unloaded += (_, _) => Unsubscribe();
		if (manager.IsLoaded)
			Subscribe();
	}

	public DockingFocusPolicy Policy { get; }

	/// <summary>Notes a user input seen anywhere in the process.</summary>
	public void NoteInput() => Policy.NoteInput();

	/// <summary>Notes a real docking change.</summary>
	public void NoteDockingChange() => Policy.NoteLayoutChange();

	public void Dispose()
	{
		Unsubscribe();
		_manager.ActiveContentChanged -= OnDockingChanged;
		_manager.PreviewLostKeyboardFocus -= OnPreviewLostKeyboardFocus;
		_layoutProperty?.RemoveValueChanged(_manager, OnLayoutReplaced);
		UnhookLayout();
	}

	private void Subscribe()
	{
		if (_subscribed)
			return;

		_subscribed = true;
		InputManager.Current.PreProcessInput += OnPreProcessInput;
		ComponentDispatcher.ThreadPreprocessMessage += OnThreadMessage;
	}

	private void Unsubscribe()
	{
		if (!_subscribed)
			return;

		_subscribed = false;
		InputManager.Current.PreProcessInput -= OnPreProcessInput;
		ComponentDispatcher.ThreadPreprocessMessage -= OnThreadMessage;
	}

	private void OnPreProcessInput(object sender, PreProcessInputEventArgs e)
	{
		if (e.StagingItem?.Input is MouseButtonEventArgs or KeyEventArgs or TouchEventArgs or StylusDownEventArgs or StylusButtonEventArgs)
			NoteInput();
	}

	private void OnThreadMessage(ref MSG msg, ref bool handled)
	{
		if (msg.message is WmLButtonDown or WmRButtonDown or WmMButtonDown or WmXButtonDown or WmKeyDown or WmSysKeyDown or WmPointerDown or WmTouch)
			NoteInput();
	}

	private void OnDockingChanged(object? sender, EventArgs e) => NoteDockingChange();

	private void OnLayoutReplaced(object? sender, EventArgs e)
	{
		UnhookLayout();
		HookLayout();
		NoteDockingChange();
	}

	private void HookLayout()
	{
		_hooked = _manager.Layout;
		if (_hooked == null)
			return;

		_hooked.Updated += OnDockingChanged;
		_hooked.ElementAdded += OnLayoutElement;
		_hooked.ElementRemoved += OnLayoutElement;
	}

	private void UnhookLayout()
	{
		if (_hooked == null)
			return;

		_hooked.Updated -= OnDockingChanged;
		_hooked.ElementAdded -= OnLayoutElement;
		_hooked.ElementRemoved -= OnLayoutElement;
		_hooked = null;
	}

	private void OnLayoutElement(object? sender, LayoutElementEventArgs e) => NoteDockingChange();

	private void OnPreviewLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
	{
		if (e.OldFocus is not DependencyObject oldFocus || e.NewFocus is not DependencyObject newFocus)
			return;

		if (newFocus is HwndHost)
			return;   // native content (the WebView2) takes the focus by a click or Tab only

		var from = PanelOf(oldFocus);
		var to = PanelOf(newFocus);
		if (Policy.IsFocusSteal(from != null, to != null && !ReferenceEquals(from, to)))
			e.Handled = true;
	}

	/// <summary>The panel (anchorable or document control) an element sits in; tab items are no panel content.</summary>
	private static DependencyObject? PanelOf(DependencyObject element)
	{
		for (var current = element; current != null; current = VisualTreeHelper.GetParent(current) ?? LogicalTreeHelper.GetParent(current))
		{
			if (current is LayoutAnchorableTabItem or LayoutDocumentTabItem)
				return null;

			if (current is LayoutAnchorableControl or LayoutDocumentControl)
				return current;
		}

		return null;
	}
}
