using AvalonDock.Layout;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TessynDesktop.WPF.Infrastructure;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.State;
using TessynDesktop.WPF.ViewModels;

namespace TessynDesktop.WPF.Views;

/// <summary>
/// <see cref="IPreviewDockFactory"/>: builds a docked panel (an AvalonDock <see cref="LayoutAnchorable"/>) with its view model and content for the router
/// (FR.29.17–FR.29.20). Resolves everything when a panel is created, not in the constructor, so the router and this factory can depend on each other without a cycle,
/// and takes the docking area from the <see cref="DockingSurfaceHost"/> the main window registers with. A group panel (<c>group</c> not null) opens at the group's
/// remembered layout; a pinned one at its own; the default Preview panel (<c>group</c> null) has the id <see cref="Constants.Previews.DefaultPanelId"/> and no Pin or
/// Auto-close.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class PreviewDockFactory : IPreviewDockFactory
{
	private readonly IServiceProvider _services;

	public PreviewDockFactory(IServiceProvider services)
	{
		_services = services;
	}

	public IPreviewWindowHandle Create(PreviewGroupModel? group, bool pinned, PreviewDockLayoutModel? ownLayout)
	{
		var surface = Get<DockingSurfaceHost>().Surface ?? throw new InvalidOperationException("The main window's docking area is not there yet.");
		var settings = Get<IPreviewSettingsService>();
		var ui = Get<IUiDispatcher>();
		var logger = Get<ILoggerFactory>().CreateLogger<DockedPreviewPanelHandle>();
		var groupId = group?.Id ?? Constants.Previews.DefaultPanelId;
		var groupName = group?.Name ?? Constants.Previews.DefaultPanelName;
		var viewModel = new PreviewWindowViewModel(groupId, groupName, pinned, settings, Get<IPreviewContentFactory>(), ui);
		var content = new PreviewPanelContent();
		content.Attach(viewModel, logger, isGroupPanel: group != null);
		var panel = new LayoutAnchorable
		{
			ContentId = pinned ? string.Format(Constants.PreviewDock.PinnedContentIdFormat, groupId, Guid.NewGuid().ToString("N")) : groupId,
			Title = viewModel.Title,
			Content = content,
			CanClose = true,
			CanFloat = true,
			CanAutoHide = true,
		};
		var remembered = pinned ? ownLayout : settings.GetDockLayout(groupId);
		return new DockedPreviewPanelHandle(viewModel, content, panel, surface, settings, remembered, ui, logger, floating: _services.GetService<IFloatingPanelPlacement>());
	}

	private T Get<T>() where T : notnull => _services.GetRequiredService<T>();
}
