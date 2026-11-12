using TessynDesktop.Models;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.Services;

/// <summary>Builds the <see cref="SessionStateDto"/> remote clients see for a session.</summary>
/// <remarks>Created by Claude</remarks>
public interface ISessionStateBuilder
{
	/// <summary>State of <paramref name="node"/> right now: running flag, streaming process, attention badge, favourite, activity.</summary>
	SessionStateDto Build(SessionNodeModel node);
}
