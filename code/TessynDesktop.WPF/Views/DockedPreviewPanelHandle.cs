using System.ComponentModel;
using AvalonDock.Layout;
using Microsoft.Extensions.Logging;
using TessynDesktop.Client;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.State;
using TessynDesktop.WPF.ViewModels;

namespace TessynDesktop.WPF.Views;

/// <summary>
/// <see cref="IPreviewWindowHandle"/> over a docked panel (FR.29.17–FR.29.21): an AvalonDock <see cref="LayoutAnchorable"/> whose content is a
/// <see cref="PreviewPanelContent"/>. The panel is created by the factory but placed in the layout by the first <see cref="Show"/> (at its remembered layout, or docked right
/// at 40 % the first time), without taking the keyboard focus. A close by the router raises <see cref="Closed"/> with <c>ByUser</c> false; the user's close (the tab's X, or
/// hiding the panel) is <c>ByUser</c> true and remembers the group's layout as closed. Closing disposes everything the panel owns: the view model with its renderer or
/// browser and their WebView2 controls. A group panel remembers its layout changes 500 ms after the last one; a pinned panel never writes its group's layout.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class DockedPreviewPanelHandle : IDockedPreviewPanelHandle
{
	private readonly PreviewWindowViewModel _viewModel;
	private readonly PreviewPanelContent _content;
	private readonly LayoutAnchorable _panel;
	private readonly IDockingSurface _surface;
	private readonly PreviewDockLayoutReader _reader;
	private readonly PreviewDockLayoutApplier _applier;
	private readonly PreviewDockLayoutTracker _tracker;
	private readonly IPreviewSettingsService _settings;
	private readonly PreviewDockLayoutModel? _remembered;
	private readonly ILogger _logger;
	private PreviewDockLayoutModel? _last;
	private PreviewDockLayoutModel? _lastDocked;
	private bool _opened;
	private bool _routerClose;
	private bool _closed;

	public DockedPreviewPanelHandle(
		PreviewWindowViewModel viewModel,
		PreviewPanelContent content,
		LayoutAnchorable panel,
		IDockingSurface surface,
		IPreviewSettingsService settings,
		PreviewDockLayoutModel? remembered,
		Infrastructure.IUiDispatcher ui,
		ILogger logger,
		TimeProvider? time = null,
		TimeSpan? layoutDelay = null)
	{
		_viewModel = viewModel;
		_content = content;
		_panel = panel;
		_surface = surface;
		_settings = settings;
		_remembered = remembered;
		_logger = logger;
		_reader = new PreviewDockLayoutReader();
		_applier = new PreviewDockLayoutApplier();
		_tracker = new PreviewDockLayoutTracker(viewModel.GroupId, settings, ui, () => _viewModel.IsPinned, Capture, time, layoutDelay);
		_tracker.PinnedLayoutChanged += (_, _) => PlacementChanged?.Invoke(this, EventArgs.Empty);
		_viewModel.Pinned += OnPinned;
		_viewModel.ItemChanged += (_, _) => ItemChanged?.Invoke(this, EventArgs.Empty);
		_viewModel.PropertyChanged += OnViewModelPropertyChanged;
		_panel.Closed += OnPanelClosed;
		_panel.PropertyChanged += OnPanelPropertyChanged;
	}

	public string GroupId => _viewModel.GroupId;

	public bool IsPinned => _viewModel.IsPinned;

	public PreviewItemModel? Item => _viewModel.Item;

	/// <summary>A panel's layout is saved by the dock layer itself (FR.29.21), so there is no window placement.</summary>
	public PreviewPlacementModel? CurrentPlacement => null;

	public PreviewDockLayoutModel? CurrentLayout => _closed || !_opened ? null : Capture()?.Clone();

	/// <summary>The panel, for tests and the dock layer.</summary>
	public LayoutAnchorable Panel => _panel;

	public event EventHandler<PreviewWindowClosedEventArgs>? Closed;

	public event EventHandler? Pinned;

	public event EventHandler? ItemChanged;

	public event EventHandler? PlacementChanged;

	public void Show(PreviewItemModel item, PreviewTarget? fileHandle, ITessynClient? client)
	{
		if (_closed)
			return;

		_viewModel.Show(item, fileHandle, client);
		if (_opened)
		{
			Reveal();
			return;
		}

		_opened = true;
		_panel.Title = _viewModel.Title;
		_applier.Apply(_surface.Root, _surface.DocumentPane, _panel, _remembered, _surface.AreaWidth, _surface.AreaHeight);
		_surface.LayoutChanged += OnLayoutChanged;
		_tracker.Settle();
		_tracker.Tracking = true;
		_logger.LogInformation(Constants.PreviewDock.PanelOpenedLog, GroupId);
	}

	public void SetGroupName(string name) => _viewModel.SetGroupName(name);

	public void Close()
	{
		if (_closed)
			return;

		_routerClose = true;
		_tracker.Changed();
		_tracker.Flush();   // FR.29.23: the layout is saved before the panel closes
		_panel.Close();
		Finish();   // a panel that was never shown raises no Closed
	}

	/// <summary>A new item for a panel that was hidden or tabbed behind another: it comes forward (no keyboard focus, FR.29.17).</summary>
	private void Reveal()
	{
		if (_panel.IsHidden)
			_panel.Show();

		_panel.IsSelected = true;
	}

	private PreviewDockLayoutModel? Capture()
	{
		if (_closed || !_opened || _panel.Root == null)
			return _last;

		_last = _reader.Read(_panel, _surface.DocumentPane, _lastDocked);
		if (!_last.Floating)
			_lastDocked = _last;

		return _last;
	}

	private void OnLayoutChanged(object? sender, EventArgs e) => _tracker.Changed();

	private void OnPinned(object? sender, EventArgs e)
	{
		_panel.ContentId = string.Format(Constants.PreviewDock.PinnedContentIdFormat, GroupId, Guid.NewGuid().ToString("N"));   // no longer its group's panel: a panel of the group never stacks "with it" by the group id
		_logger.LogInformation(Constants.PreviewDock.PanelPinnedLog, GroupId);
		Pinned?.Invoke(this, EventArgs.Empty);
	}

	private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName == nameof(PreviewWindowViewModel.Title))
			_panel.Title = _viewModel.Title;
	}

	/// <summary>The user hid the panel (its X on a tool window): it is closed for good, as a tab's X does.</summary>
	private void OnPanelPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName != nameof(LayoutAnchorable.IsHidden) || !_panel.IsHidden || _closed || !_opened)
			return;

		_panel.Close();
		Finish();
	}

	private void OnPanelClosed(object? sender, EventArgs e) => Finish();

	private void Finish()
	{
		if (_closed)
			return;

		if (!_routerClose && !_viewModel.IsPinned && _last != null)
		{
			var layout = _last.Clone();
			layout.Shown = false;   // the group's next item opens it again at this layout (FR.29.18)
			_settings.SaveDockLayout(GroupId, layout);
		}

		_closed = true;
		_surface.LayoutChanged -= OnLayoutChanged;
		_panel.Closed -= OnPanelClosed;
		_panel.PropertyChanged -= OnPanelPropertyChanged;
		_viewModel.PropertyChanged -= OnViewModelPropertyChanged;
		_tracker.Dispose();
		_content.Detach();
		_viewModel.Dispose();
		_logger.LogInformation(Constants.PreviewDock.PanelClosedLog, GroupId, !_routerClose);
		Closed?.Invoke(this, new PreviewWindowClosedEventArgs(byUser: !_routerClose));
	}
}
