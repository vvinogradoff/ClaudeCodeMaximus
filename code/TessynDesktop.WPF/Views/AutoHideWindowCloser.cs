using System.Reflection;
using Microsoft.Extensions.Logging;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using AvalonDock;
using AvalonDock.Controls;
using AvalonDock.Layout;

namespace TessynDesktop.WPF.Views;

/// <summary>
/// Closes the slide-out of an auto-hidden panel (FR.6.13, FR.29 decision 4): Esc while the keyboard focus is in it, and a click on its edge tab while it is open (the library
/// ignores that click). The library offers no public way to hide the slide-out, so its internal <c>DockingManager.HideAutoHideWindow</c> is called; where it is missing
/// (another library version) nothing is closed and <see cref="Available"/> is false.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class AutoHideWindowCloser
{
	private readonly DockingManager _manager;
	private readonly DockingVisualTree _tree = new();
	private MethodInfo? _hide;
	private ILogger? _logger;
	private bool _failureLogged;
	private bool _handlerFailureLogged;

	public AutoHideWindowCloser(DockingManager manager)
	{
		_manager = manager;
		_hide = FindHide();
		manager.PreviewKeyDown += OnPreviewKeyDown;
		manager.PreviewMouseDown += OnPreviewMouseDown;
	}

	public bool Available => _hide != null;

	/// <summary>Gives the closer a log and logs, once, whether the library's method was found.</summary>
	public void UseLogger(ILogger logger)
	{
		_logger = logger;
		if (_hide != null)
			logger.LogInformation("Docking: the slide-out can be closed with Esc and the edge tab (library method {Method} found)", Constants.DockingChrome.HideAutoHideWindowMethod);
		else
			logger.LogWarning("Docking: the library method {Method} was not found; Esc and the edge tab do not close the slide-out", Constants.DockingChrome.HideAutoHideWindowMethod);
	}

	/// <summary>The library's internal method by its exact shape (one <see cref="LayoutAnchorControl"/> parameter); null when it is missing, ambiguous or the lookup fails.</summary>
	private static MethodInfo? FindHide()
	{
		try
		{
			return typeof(DockingManager).GetMethod(Constants.DockingChrome.HideAutoHideWindowMethod, BindingFlags.Instance | BindingFlags.NonPublic, null, [typeof(LayoutAnchorControl)], null);
		}
		catch (Exception)
		{
			return null;
		}
	}

	/// <summary>True while a slide-out is open.</summary>
	public bool IsOpen => _manager.AutoHideWindow is { Model: not null, Visibility: Visibility.Visible };

	/// <summary>True while the open slide-out shows <paramref name="element"/> (an auto-hide group or the panel itself).</summary>
	public bool IsOpenFor(ILayoutElement? element) => element != null && IsOpen && ReferenceEquals(_manager.AutoHideWindow.Model, element);

	/// <summary>Closes the open slide-out; false when none is open or the library has no such method.</summary>
	public bool Close()
	{
		if (_hide == null || !IsOpen)
			return false;

		try
		{
			_hide.Invoke(_manager, [null]);
			return true;
		}
		catch (Exception ex)
		{
			if (!_failureLogged)
			{
				_failureLogged = true;
				_logger?.LogWarning(ex, "Docking: closing the slide-out through the library method {Method} failed; Esc and the edge tab no longer close it", Constants.DockingChrome.HideAutoHideWindowMethod);
			}

			_hide = null;   // Available is false from now on
			return false;
		}
	}

	private void OnPreviewKeyDown(object sender, KeyEventArgs e)
	{
		try
		{
			if (e.Key != Key.Escape || e.Handled || !IsOpen)
				return;

			if (e.OriginalSource is DependencyObject source && IsInside(source, _manager.AutoHideWindow) && Close())
				e.Handled = true;
		}
		catch (Exception ex)
		{
			LogHandlerFailure(ex);
		}
	}

	private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
	{
		try
		{
			if (e.Handled || !IsOpen || e.OriginalSource is not DependencyObject source)
				return;

			var anchor = Ancestor<LayoutAnchorControl>(source);
			if (anchor != null && ReferenceEquals(anchor.Model, _manager.AutoHideWindow.Model) && Close())
				e.Handled = true;   // the click on the edge tab of the open slide-out closes it
		}
		catch (Exception ex)
		{
			LogHandlerFailure(ex);
		}
	}

	/// <summary>Input handlers of the docking chrome never throw: the failure is logged once and the key or click goes on untouched.</summary>
	private void LogHandlerFailure(Exception ex)
	{
		if (_handlerFailureLogged)
			return;

		_handlerFailureLogged = true;
		_logger?.LogWarning(ex, "Docking: the slide-out input handler failed; the input proceeds untouched");
	}

	private bool IsInside(DependencyObject element, DependencyObject container)
	{
		for (var current = element; current != null; current = _tree.ParentOf(current))
		{
			if (ReferenceEquals(current, container))
				return true;
		}

		return false;
	}

	private T? Ancestor<T>(DependencyObject element) where T : DependencyObject
	{
		for (var current = element; current != null; current = _tree.ParentOf(current))
		{
			if (current is T match)
				return match;
		}

		return null;
	}
}
