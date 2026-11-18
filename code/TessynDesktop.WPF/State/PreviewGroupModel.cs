namespace TessynDesktop.WPF.State;

/// <summary>A user-defined kind of previewable item (Settings → Previews): its patterns decide which items open in the group's window.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class PreviewGroupModel
{
	/// <summary>Stable id: a fixed readable one for the defaults, a 32-digit hex Guid for groups the user added.</summary>
	public string Id { get; set; } = string.Empty;

	public string Name { get; set; } = string.Empty;

	/// <summary>The patterns as typed: file extensions and URL patterns separated by spaces and/or commas.</summary>
	public string Patterns { get; set; } = string.Empty;

	/// <summary>The group's window closes when the Focus item it came from is resolved or another session is selected.</summary>
	public bool AutoClose { get; set; }

	public PreviewGroupModel Clone() => (PreviewGroupModel)MemberwiseClone();
}
