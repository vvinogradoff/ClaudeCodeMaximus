using TessynDesktop.Models.Remote;

namespace TessynDesktop.WPF.Tests.Fakes;

/// <summary>Helpers that build tree DTOs for tests.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class TreeBuilder
{
	public TreeSessionDto Session(string id, string name = "", int workers = 0, bool running = false, string? supervisor = null) =>
		new(id, string.IsNullOrEmpty(name) ? id : name, workers, supervisor, new SessionStateDto(id, running, false, null, false, null, null, false, null));

	public TreeGroupDto Group(string id, string name, params TreeSessionDto[] sessions) =>
		new(id, name, false, [], sessions);

	public TreeDirectoryDto Directory(string id, string label, IReadOnlyList<TreeGroupDto> groups, params TreeSessionDto[] sessions) =>
		new(id, label, false, groups, sessions);
}
