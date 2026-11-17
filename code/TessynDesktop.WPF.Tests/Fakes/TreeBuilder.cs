using TessynDesktop.Models.Remote;

namespace TessynDesktop.WPF.Tests.Fakes;

/// <summary>Helpers that build tree DTOs for tests.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class TreeBuilder
{
	public TreeSessionDto Session(string id, string name = "", int workers = 0, bool running = false, string? supervisor = null) =>
		new(id, string.IsNullOrEmpty(name) ? id : name, workers, supervisor, new SessionStateDto(id, running, false, null, false, null, null, false, null));

	/// <summary>A top-level session with a given live state.</summary>
	public TreeSessionDto SessionWith(SessionStateDto state, string name = "") =>
		new(state.Id, string.IsNullOrEmpty(name) ? state.Id : name, 0, null, state);

	public TreeGroupDto Group(string id, string name, params TreeSessionDto[] sessions) =>
		new(id, name, false, [], sessions);

	/// <summary>A group the daemon flags as holding only workers (an orchestration husk).</summary>
	public TreeGroupDto Husk(string id, string name) => new(id, name, false, [], [], HoldsOnlyWorkers: true);

	public TreeDirectoryDto Directory(string id, string label, IReadOnlyList<TreeGroupDto> groups, params TreeSessionDto[] sessions) =>
		new(id, label, false, groups, sessions);
}
