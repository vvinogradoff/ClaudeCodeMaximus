using System.Text.Json;

namespace TessynDesktop.WPF.State;

/// <summary>One stored settings section: its schema version and its JSON, which only the owning section class interprets.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class ClientSectionModel
{
	public int Version { get; set; }

	public JsonElement Data { get; set; }
}
