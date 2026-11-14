namespace TessynDesktop.WPF.State;

/// <summary>
/// The WPF client's own state, persisted in <c>%AppData%\TessynDesktop\clients\wpf.json</c> (P7: the daemon owns shared state,
/// each client owns its layout, selection and presentation state).
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class ClientStateModel
{
	public int Version { get; set; } = 1;

	public WindowPlacementModel? Window { get; set; }

	/// <summary>Width of the tree column in device-independent pixels (the splitter position).</summary>
	public double TreeWidth { get; set; } = Constants.Limits.DefaultTreeWidth;

	public string? LastSelectedNodeId { get; set; }

	/// <summary>Expanded directories, groups and sessions; null until the user changed an expansion (then the daemon's flags seed it).</summary>
	public List<string>? ExpandedNodeIds { get; set; }
}
