using System.Text.Json;

namespace TessynDesktop.Codex;

/// <summary>A notification from the app-server (a message with <c>method</c> and no <c>id</c>). <see cref="Params"/> is a detached clone.</summary>
/// <remarks>Created by Claude</remarks>
public sealed record CodexNotification(string Method, JsonElement Params);
