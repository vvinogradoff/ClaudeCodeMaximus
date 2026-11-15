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

	/// <summary>The id this install gives the daemon when it registers the channels it presents (<c>client.register</c>); created once, so a reconnect or a restart counts as the same client.</summary>
	public string? ClientId { get; set; }

	/// <summary>Expanded directories, groups and sessions; null until the user changed an expansion (then the daemon's flags seed it).</summary>
	public List<string>? ExpandedNodeIds { get; set; }

	/// <summary>The composer's file mode (FR.31.2): dropped files are copied into the session's attachments instead of inserting their paths. Global, not per session.</summary>
	public bool AttachAsFiles { get; set; }

	/// <summary>Typed, versioned settings sections by name (see <see cref="IClientSettingsStore"/>); new sections never need a new property here.</summary>
	public Dictionary<string, ClientSectionModel> Sections { get; set; } = new();
}
