using TessynDesktop.Models.Remote;

namespace TessynDesktop.Services;

/// <summary>The Local Tool Profile of a directory (FR.12.21) for <c>localTools.get</c> and <c>localTools.set</c> (P1.J1).</summary>
/// <remarks>Created by Claude</remarks>
public interface ILocalToolsService
{
	/// <summary>The directory's profile (the default lean profile when none is stored) and the selectable tools. Throws <see cref="TdcpException"/> for an unknown directory.</summary>
	LocalToolsDto Get(string directoryId);

	/// <summary>
	/// Stores the profile, saves, and raises a tree change. Unknown native tool names are refused (<see cref="TdcpInvalidParamsException"/>);
	/// MCP server ids that are no longer registered are dropped, as the desktop's checklist does. Answers like <see cref="Get"/>.
	/// </summary>
	LocalToolsDto Set(string directoryId, bool enabled, IReadOnlyList<string> nativeTools, bool includeCmxMcp, IReadOnlyList<string> includedMcpServerIds);
}
