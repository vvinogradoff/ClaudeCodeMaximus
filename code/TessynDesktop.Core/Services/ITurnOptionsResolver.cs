using System.Text.Json.Nodes;
using System.Threading.Tasks;
using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <summary>
/// Reads the optional <c>modelId</c>, <c>effort</c> and <c>profileId</c> params of session.send, attention.reply and
/// session.compact and turns them into per-turn options, validating each against the catalog.
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface ITurnOptionsResolver
{
	/// <summary>
	/// The options, or null when none of the three params is present. An unknown model, effort or profile throws
	/// <see cref="TdcpInvalidParamsException"/>. <c>profileId: "default"</c> becomes the Default profile's "[Sys] ..." label.
	/// </summary>
	Task<TurnOptionsModel?> ResolveAsync(JsonNode? @params);
}
