using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Models.Turns;

namespace TessynDesktop.Services;

/// <summary>
/// Resolves the launch settings of a turn requested over the wire, where no command bar made the choices: the per-turn options first, then
/// what the session, its directory and the app settings say (the same resolution a headless turn applies).
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface ITurnSettingsResolver
{
	/// <summary>Model, effort, profile (config dir and stamped label), local-model routing and tool support of a turn on <paramref name="node"/>.</summary>
	Task<TurnSettings> ResolveAsync(SessionNodeModel node, TurnOptionsModel? options);
}
