using System.Text.Json;

namespace TessynDesktop.Tests.Codex.Fakes;

/// <summary>The <c>turn/start</c> a turn script is built for: the thread, the turn id the fake gave it and the params Tessyn sent.</summary>
/// <remarks>Created by Claude</remarks>
internal sealed record FakeTurnStart(string ThreadId, string TurnId, JsonElement Params);
