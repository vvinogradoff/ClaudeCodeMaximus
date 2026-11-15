namespace TessynDesktop.WPF.ViewModels.Settings;

/// <summary>Shows the MCP server editor modally and returns when the user closed it (a test presenter drives the view model instead).</summary>
/// <remarks>Created by Claude</remarks>
public interface IMcpServerEditorPresenter
{
	Task ShowAsync(McpServerEditorViewModel editor);
}
