using System.Windows;
using System.Windows.Data;
using Microsoft.Extensions.Logging;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.ViewModels;

namespace TessynDesktop.WPF.Views;

/// <summary>
/// Creates the contents of the shell panels for the docking area (FR.6.11): Sessions (the tree panel on the main view model's tree), Recent and Tags (the contributions of the
/// left-panel slot, by their panel ids) and Focus (the focus panel on the session pane's Focus view model, which exists once the client is connected).
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class ShellPanelCatalog
{
	public IReadOnlyList<ShellPanelDefinition> Create(MainViewModel main, IShellSlots slots, ILogger logger)
	{
		var panels = new List<ShellPanelDefinition>();
		var sessions = new TreePanel();
		sessions.SetBinding(FrameworkElement.DataContextProperty, new Binding(nameof(MainViewModel.Tree)) { Source = main });
		panels.Add(new ShellPanelDefinition { Id = Constants.DockedShell.SessionsPanelId, Title = Constants.ShellLayout.SessionsTitle, Content = sessions, IsLeft = true });
		foreach (var contribution in slots.For(Constants.Slots.LeftPanelTabs))
		{
			if (contribution.PanelId == null || SlotContributions.TryCreate(contribution, logger) is not FrameworkElement view)
				continue;

			panels.Add(new ShellPanelDefinition { Id = contribution.PanelId, Title = contribution.Title ?? contribution.PanelId, Content = view, IsLeft = true });
		}

		var focus = new FocusPanel();
		focus.SetBinding(FrameworkElement.DataContextProperty, new Binding($"{nameof(MainViewModel.Session)}.{nameof(SessionPaneViewModel.Focus)}") { Source = main });
		focus.SetBinding(FocusPanel.SessionProperty, new Binding(nameof(MainViewModel.Session)) { Source = main });   // the header acts on the session the Session view shows (FR.6.23)
		panels.Add(new ShellPanelDefinition { Id = Constants.DockedShell.FocusPanelId, Title = Constants.ShellLayout.FocusTitle, Content = focus });
		return panels;
	}
}
