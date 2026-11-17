namespace TessynDesktop.WPF.ViewModels;

public sealed partial class SessionPaneViewModel
{
	private TagAreaViewModel? _tagArea;

	/// <summary>The tag chips with their mini/full mode (FR.24); created on first use.</summary>
	public TagAreaViewModel TagArea => _tagArea ??= new TagAreaViewModel(Tags);

	/// <summary>Another session is opened (or none): the tag area starts collapsed again (FR.24). Not called for a Session/Focus view switch.</summary>
	private void ResetTagArea()
	{
		if (_tagArea != null)
			_tagArea.IsExpanded = false;
	}
}
