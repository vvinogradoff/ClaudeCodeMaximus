using System.Text.Json.Nodes;

namespace TessynDesktop.Models;

/// <summary>The arguments of <c>request_elevated_run</c> as the tool received them (FR.15.16); the service checks every one of them.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class ElevatedRunRequestModel
{
	public string? Command { get; init; }
	public string? WorkingDir { get; init; }
	public string? Reason { get; init; }

	/// <summary>As given: absent, a whole number, or anything else (which is an error, never clamped).</summary>
	public JsonNode? TimeoutMinutes { get; init; }
}
