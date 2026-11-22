using System.Text.Json;

namespace TessynDesktop.Codex.Protocol;

/// <summary>A turn error. <c>codexErrorInfo</c> is an open union (a string or a one-member object), kept raw (FR.33.13).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class CodexTurnError
{
	public string Message { get; set; } = null!;
	public JsonElement? CodexErrorInfo { get; set; }
	public string? AdditionalDetails { get; set; }
}
