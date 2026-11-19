using System.Windows;
using System.Windows.Automation;
using Microsoft.Extensions.Logging;
using TessynDesktop.WPF.ViewModels;

namespace TessynDesktop.WPF.Views;

/// <summary>
/// The content of a docked preview panel (FR.29.18, FR.29.20): a compact button row (the file's Source and Open in default app; Pin and Auto-close of a group panel)
/// over the shared <see cref="PreviewContentHost"/>. The default Preview panel has neither Pin nor Auto-close. Every button has an automation name that carries the
/// group, so the panels' buttons are unique.
/// </summary>
/// <remarks>Created by Claude</remarks>
public partial class PreviewPanelContent : System.Windows.Controls.UserControl
{
	public PreviewPanelContent()
	{
		InitializeComponent();
	}

	/// <summary>The content host, for tests.</summary>
	public PreviewContentHost Host => ContentView;

	/// <summary>Binds the panel to <paramref name="viewModel"/>; <paramref name="isGroupPanel"/> false hides Pin and Auto-close (the default Preview panel).</summary>
	public void Attach(PreviewWindowViewModel viewModel, ILogger logger, bool isGroupPanel)
	{
		DataContext = viewModel;
		var name = viewModel.GroupName;
		AutomationProperties.SetAutomationId(this, string.Format(Constants.PreviewDock.PanelAutomationIdFormat, viewModel.GroupId));
		AutomationProperties.SetName(PinButton, string.Format(Constants.PreviewDock.PinAutomationNameFormat, name));
		AutomationProperties.SetName(AutoCloseButton, string.Format(Constants.PreviewDock.AutoCloseAutomationNameFormat, name));
		AutomationProperties.SetName(SourceButton, string.Format(Constants.PreviewDock.SourceAutomationNameFormat, name));
		AutomationProperties.SetName(OpenExternalButton, string.Format(Constants.PreviewDock.OpenExternalAutomationNameFormat, name));
		GroupButtons.Visibility = isGroupPanel ? Visibility.Visible : Visibility.Collapsed;
		ContentView.Attach(viewModel, logger);
	}

	/// <summary>Stops following the view model (the panel closed).</summary>
	public void Detach()
	{
		ContentView.Detach();
		DataContext = null;
	}
}
