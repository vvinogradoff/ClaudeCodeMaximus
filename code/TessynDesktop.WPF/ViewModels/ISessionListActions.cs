namespace TessynDesktop.WPF.ViewModels;

/// <summary>What a session list (Recent, Tags) needs from the shell: the actions every row offers. Implemented by <see cref="SessionListsHost"/>.</summary>
/// <remarks>Created by Claude</remarks>
public interface ISessionListActions
{
	/// <summary>Opens the session in the pane (and selects it in the tree).</summary>
	void Open(string nodeId, string name);

	/// <summary>Sets the favourite flag through <c>session.setFavorite</c>; returns the value the daemon stored, or null when it failed.</summary>
	Task<bool?> SetFavoriteAsync(string nodeId, bool value);
}
