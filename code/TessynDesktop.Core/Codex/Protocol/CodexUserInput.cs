using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TessynDesktop.Codex.Protocol;

/// <summary>One input item of <c>turn/start</c> (open union on <c>type</c>; Tessyn sends <c>text</c> and <c>localImage</c>, FR.33.8).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class CodexUserInput
{
	public string Type { get; set; } = null!;
	public string? Text { get; set; }
	public string? Path { get; set; }
	[JsonPropertyName("text_elements")]
	public List<JsonElement>? TextElements { get; set; }

	/// <summary>A <c>text</c> item: exactly what the Claude engine would send on stdin (FR.33.8).</summary>
	public static CodexUserInput FromText(string text) => new() { Type = Constants.Codex.Values.UserInputText, Text = text };

	/// <summary>A <c>localImage</c> item for an attached image, when the model lists <c>image</c> in its input modalities (FR.33.8).</summary>
	public static CodexUserInput FromLocalImage(string path) => new() { Type = Constants.Codex.Values.UserInputLocalImage, Path = path };
}
