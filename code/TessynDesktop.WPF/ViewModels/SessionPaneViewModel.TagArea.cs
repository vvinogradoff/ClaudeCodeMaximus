namespace TessynDesktop.WPF.ViewModels;

public sealed partial class SessionPaneViewModel
{
	private TagAreaViewModel? _tagArea;

	/// <summary>The tag chips with their mini/full mode (FR.24); created on first use, from the client state store the pane was given.</summary>
	public TagAreaViewModel TagArea => _tagArea ??= new TagAreaViewModel(Tags, _stateStore);
}
