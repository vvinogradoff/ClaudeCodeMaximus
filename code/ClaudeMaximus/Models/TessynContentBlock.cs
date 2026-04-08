using System.Text.Json.Serialization;

namespace ClaudeMaximus.Models;

/// <summary>
/// A single content block in a multimodal message, matching the Anthropic Messages API.
/// Used as the new content[] format for run.send (daemon v0.4+).
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class TessynContentBlock
{
    /// <summary>Block type: "text" or "image".</summary>
    public required string Type { get; init; }

    /// <summary>Plain text. Required when Type == "text".</summary>
    public string? Text { get; init; }

    /// <summary>Image source. Required when Type == "image".</summary>
    public TessynImageSource? Source { get; init; }

    public static TessynContentBlock FromText(string text) =>
        new() { Type = "text", Text = text };

    public static TessynContentBlock FromBase64Image(string mediaType, string base64Data) =>
        new()
        {
            Type = "image",
            Source = new TessynImageSource
            {
                Type = "base64",
                MediaType = mediaType,
                Data = base64Data,
            },
        };
}

/// <summary>
/// Image source descriptor for an image content block.
/// </summary>
public sealed class TessynImageSource
{
    /// <summary>Source type, currently always "base64".</summary>
    public required string Type { get; init; }

    /// <summary>Media type, e.g. "image/png", "image/jpeg".</summary>
    [JsonPropertyName("media_type")]
    public required string MediaType { get; init; }

    /// <summary>Base64-encoded image bytes (no data: URI prefix).</summary>
    public required string Data { get; init; }
}
