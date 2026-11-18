using TessynDesktop.Client;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.State;
using TessynDesktop.WPF.ViewModels;

namespace TessynDesktop.WPF.Views;

/// <summary>
/// <see cref="IPreviewWindowHandle"/> over a <see cref="PreviewWindow"/> and its view model (FR.29.5, FR.29.8). The window is created by the factory but opened by
/// the first <see cref="Show"/>, without activating it. A close by the router raises <see cref="Closed"/> with <c>ByUser</c> false; any other close is the user's.
/// Closing disposes everything the window owns: the view model with its renderer or browser and their WebView2 controls.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class PreviewWindowHandle : IPreviewWindowHandle
{
	private readonly PreviewWindow _window;
	private readonly PreviewWindowViewModel _viewModel;
	private bool _opened;
	private bool _routerClose;
	private bool _closed;

	public PreviewWindowHandle(PreviewWindowViewModel viewModel, PreviewWindow window)
	{
		_viewModel = viewModel;
		_window = window;
		_viewModel.Pinned += (_, _) => Pinned?.Invoke(this, EventArgs.Empty);
		_viewModel.ItemChanged += (_, _) => ItemChanged?.Invoke(this, EventArgs.Empty);
		_window.PinnedPlacementChanged += (_, _) => PlacementChanged?.Invoke(this, EventArgs.Empty);
		_window.Closed += OnWindowClosed;
	}

	public string GroupId => _viewModel.GroupId;

	public bool IsPinned => _viewModel.IsPinned;

	public PreviewItemModel? Item => _viewModel.Item;

	public PreviewPlacementModel? CurrentPlacement => _closed ? null : _window.CurrentPlacement();

	public event EventHandler<PreviewWindowClosedEventArgs>? Closed;

	public event EventHandler? Pinned;

	public event EventHandler? ItemChanged;

	public event EventHandler? PlacementChanged;

	public void Show(PreviewItemModel item, PreviewTarget? fileHandle, ITessynClient? client)
	{
		if (_closed)
			return;

		_viewModel.Show(item, fileHandle, client);
		if (!_opened)
		{
			_opened = true;
			_window.Show();   // ShowActivated is false
			return;
		}

		_window.RaiseWithoutActivation();
	}

	public void SetGroupName(string name) => _viewModel.SetGroupName(name);

	public void Close()
	{
		if (_closed)
			return;

		_routerClose = true;
		_window.Close();
		Finish();   // a window that was never shown may not raise Closed
	}

	private void OnWindowClosed(object? sender, EventArgs e) => Finish();

	private void Finish()
	{
		if (_closed)
			return;

		_closed = true;
		_window.Closed -= OnWindowClosed;
		_viewModel.Dispose();
		Closed?.Invoke(this, new PreviewWindowClosedEventArgs(byUser: !_routerClose));
	}
}
