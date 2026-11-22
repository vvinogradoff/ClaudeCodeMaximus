using System.Collections.Generic;

namespace TessynDesktop.Services;

/// <summary>
/// Which session engines can run here (tdcp-spec 10.A14, <c>client.hello</c>'s <c>engines</c>): the registered engines whose CLI was found.
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface ISessionEngineAvailability
{
	/// <summary>The available engine ids in registration order (<c>claude</c> first). Cheap: file checks only, no process is started.</summary>
	IReadOnlyList<string> GetAvailableEngines();
}
