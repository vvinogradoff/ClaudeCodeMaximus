using System.Text.Json;

namespace TessynDesktop.Codex;

/// <summary>A request the app-server sends to Tessyn (a message with both <c>method</c> and <c>id</c>); it must always be answered (FR.33.8).</summary>
/// <remarks>Created by Claude</remarks>
public sealed record CodexServerRequest(JsonElement Id, string Method, JsonElement Params);
