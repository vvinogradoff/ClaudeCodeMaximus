namespace TessynDesktop.ViewModels;

/// <summary>
/// Lightweight view model for a single entry in the import picker session preview panel.
/// Shows role and truncated content for user/assistant exchanges.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class PreviewEntryViewModel
{
	public string RoleLabel { get; }
	public string Content { get; }
	public bool IsUser { get; }
	public bool IsAssistant { get; }
	public bool IsSystem { get; }

	public PreviewEntryViewModel(string role, string content)
	{
		RoleLabel = role;
		Content = content;
		IsUser = role == Constants.SessionFile.RoleUser;
		IsAssistant = role == Constants.SessionFile.RoleAssistant;
		IsSystem = role == Constants.SessionFile.RoleSystem;
	}
}
